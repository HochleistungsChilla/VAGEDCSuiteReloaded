//---------------------------------------------------------------------------
//
// Reloaded/Gdi3dSurface.cs
//
// 3D surface view for the Kennfeld viewer: renders the SAME double[,] the 2D
// OxyPlot heat map draws as a shaded, rotate-and-zoom 3D surface using nothing
// but System.Drawing (GDI+) and System.Windows.Forms.
//
// This is the spike from tmp/gdi3d-spike (61-62 fps on 16x8, 54 fps on 64x32
// under Wine/Xvfb) lifted into an embeddable Control; the render algorithm is
// unchanged on purpose - only the data source, the axes, the palette and the
// host plumbing are new.  See VIEWER3D.md for the integration notes.
//
// Pipeline (identical to the spike):
//   * grid -> world:  x = (u - 0.5) * 2.4, y = (0.5 - v) * 1.2,
//                     z = (t - 0.45) * 0.9   with t = value normalized to 0..1
//   * yaw around world Z, then pitch around world X, then a perspective divide
//     with the camera at (0, 0, 4.2) looking down -Z.
//   * hidden-surface removal: a software Z-buffer.  Every grid quad becomes two
//     triangles rasterised into int[] colour + float[] 1/depth buffers.  Exact,
//     unlike painter sorting (which was wrong at steep folds).
//   * flat shading: per-quad average value -> green/yellow/orange/red ramp,
//     modulated by a Lambert term from a fixed camera-space light.
//   * wireframe edges are 3D DDA lines depth-tested against the same buffer,
//     drawn in crisp black over the shaded fill (the original Nevron look).
//   * no backface culling: the surface is an open heightfield, culling punches
//     holes at silhouettes and the Z-buffer already stops far-side overdraw.
//
// Rendering happens only when the control is invalidated (drag / wheel / reset
// / theme change / resize).  There is no animation timer and no downsampling:
// every cell of the map is drawn.
//
// Input: left-drag rotates (yaw/pitch), the wheel zooms on the cursor, and
// right-/middle-drag pans; double-click or the Reset button restores the
// default camera.  The frame colour follows the host palette (luminance of
// Parent.BackColor, see MapViewForm.ApplyHostPalette) - the black wireframe
// stays black in both themes because the surface is always the value ramp.
//
//---------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Reloaded
{
    internal struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>
    /// Embeddable 3D surface of one map.  Holds a reference to the viewer's
    /// shared <c>double[,]</c> (indexed <c>[column, row]</c>, the same array the
    /// heat map and the cell grid use), so a committed cell edit shows up the
    /// next time the surface is painted - no copy, no invalidation plumbing.
    /// </summary>
    internal sealed class SurfaceView : Control
    {
        // ---- map data (owned by MapViewForm / MapCellGrid) --------------
        private readonly double[,] _values;   // [column, row] - shared, never copied
        private readonly double[] _xValues;   // one real axis value per column
        private readonly double[] _yValues;   // one real axis value per row
        private readonly string _xTitle;      // caption incl. unit, as in the 2D chart
        private readonly string _yTitle;
        private readonly int _cols;           // = values.GetLength(0)
        private readonly int _rows;           // = values.GetLength(1)

        // Per-paint world heights [row, column] (rebuilt from _values, so edits
        // and a changing value range are always current).
        private double[,] _z;

        // ---- camera / interaction ---------------------------------------
        private double _yaw = -0.62;          // radians around world Z
        private double _pitch = 0.92;         // radians around X (0 = edge, PI/2 = top)
        private double _zoom = 1.0;
        private double _panX;                 // screen-pixel pan offset (pan + cursor zoom)
        private double _panY;
        private const double DefaultYaw = -0.62;
        private const double DefaultPitch = 0.92;
        private const double DefaultZoom = 1.0;
        private const double CamDist = 4.2;
        private const double BaseK = 1.45;    // screen scale multiplier

        private bool _dragging;
        private bool _panning;                // true = right/middle drag pans
        private Point _lastMouse;
        private bool _userZoomed;             // false = follow the fit zoom on resize
        private double _fitZoom = DefaultZoom;

        // ---- raster buffers (reused until the size changes) -------------
        private Bitmap _bmp;
        private int[] _pixels;
        private float[] _zbuf;
        private int _bw, _bh;

        // ---- palette -----------------------------------------------------
        private bool _dark = true;
        private Color _bg = Color.FromArgb(30, 33, 38);
        private Color _text = Color.FromArgb(230, 232, 235);
        private Color _muted = Color.FromArgb(154, 163, 173);
        private Color _frame = Color.FromArgb(52, 58, 66);
        private int _bgArgb = Color.FromArgb(30, 33, 38).ToArgb();
        private bool _paletteReady;

        // ---- timing / HUD ------------------------------------------------
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastPaintMs;
        private int _frames;
        private double _fps;
        private double _fpsWindowStart;

        // Optional measurement hook: when RELOADED_3D_FPS_LOG names a file, one
        // line per fps window (fps + smoothed paint ms) is appended.  Off by
        // default; used for the headless numbers in VIEWER3D.md.
        private readonly string _fpsLogPath;

        private readonly Font _tickFont = new Font("Arial", 7.5f, FontStyle.Regular);
        private readonly Font _hudFont = new Font("Arial", 8f, FontStyle.Regular);
        private readonly Font _captionFont = new Font("Arial", 8f, FontStyle.Bold);

        private WheelRouter _router;
        private Control _host;                // palette owner (viewer surface)

        public SurfaceView(
            double[,] values,
            double[] xValues,
            double[] yValues,
            string xTitle,
            string yTitle)
        {
            if (values == null) throw new ArgumentNullException("values");
            if (xValues == null) throw new ArgumentNullException("xValues");
            if (yValues == null) throw new ArgumentNullException("yValues");

            _values = values;
            _xValues = xValues;
            _yValues = yValues;
            _xTitle = xTitle ?? "X";
            _yTitle = yTitle ?? "Y";
            _cols = Math.Max(1, values.GetLength(0));
            _rows = Math.Max(1, values.GetLength(1));

            _fpsLogPath = Environment.GetEnvironmentVariable("RELOADED_3D_FPS_LOG");
            if (_fpsLogPath != null && _fpsLogPath.Length == 0) _fpsLogPath = null;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable |
                     ControlStyles.ResizeRedraw, true);
            TabStop = true;
            BackColor = _bg;
            // Crosshair over the diagram = the zoom anchor; the grab hand
            // appears while dragging (see OnMouseDown/OnMouseUp).
            Cursor = Cursors.Cross;

            _router = WheelRouter.Install(this);
            _fpsWindowStart = _clock.Elapsed.TotalSeconds;
        }

        /// <summary>Paint cost of the last frame (smoothed, milliseconds).</summary>
        public double LastPaintMs { get { return _lastPaintMs; } }

        /// <summary>Measured frames per second while the user was interacting.</summary>
        public double Fps { get { return _fps; } }

        // ------------------------------------------------------------------
        // host palette
        // ------------------------------------------------------------------

        /// <summary>
        /// Derives the surface palette from the active theme data.  The
        /// <paramref name="hostBack"/> parameter is kept for the caller but is
        /// no longer used — the 3D view reads <see cref="ThemeStore.Active"/>
        /// so it always matches the other cards, in any of the four themes.
        /// </summary>
        public void ApplyPalette(Color hostBack)
        {
            UiPalette c = ThemeStore.Active;
            bool dark = c.Dark;

            Color bg = c.Card;
            Color text = c.Text;
            Color muted = c.Muted;
            Color frame = c.Line;

            bool changed = !_paletteReady || dark != _dark || bg != _bg ||
                           text != _text || frame != _frame;
            _dark = dark;
            _bg = bg;
            _bgArgb = bg.ToArgb();
            _text = text;
            _muted = muted;
            _frame = frame;
            _paletteReady = true;

            BackColor = bg;
            ForeColor = text;
            if (changed && IsHandleCreated) Invalidate();
        }

        /// <summary>Re-reads the host background (theme switch, reparent).</summary>
        private void SyncPalette()
        {
            // The control lives in a docked panel; the palette owner is the
            // viewer surface one level up (Main's viewer body inline, or the
            // MapViewForm itself when the form is shown on its own) - the same
            // control MapCellGrid reads its palette from.
            Control host = Parent;
            if (host != null && host.Parent != null) host = host.Parent;
            if (host == null) return;

            if (!ReferenceEquals(host, _host))
            {
                if (_host != null) _host.BackColorChanged -= HostBackColorChanged;
                _host = host;
                _host.BackColorChanged += HostBackColorChanged;
            }
            ApplyPalette(host.BackColor);
        }

        /// <summary>
        /// A live theme change repaints the whole chrome through BackColor, so
        /// follow it here instead of waiting for the next mouse event.
        /// </summary>
        private void HostBackColorChanged(object sender, EventArgs e)
        {
            if (_host != null) ApplyPalette(_host.BackColor);
        }

        // ------------------------------------------------------------------
        // public view commands
        // ------------------------------------------------------------------

        /// <summary>Restores the default camera (the Reset button / Home key).</summary>
        public void ResetView()
        {
            _yaw = DefaultYaw;
            _pitch = DefaultPitch;
            _panX = 0;
            _panY = 0;
            _userZoomed = false;
            _zoom = ComputeFitZoom(ClientSize.Width, ClientSize.Height);
            Invalidate();
        }

        /// <summary>
        /// Zoom that fits the default camera into the current client area.
        /// The projection is linear in zoom, so measuring the world box at
        /// zoom 1 and dividing the usable area by its extent is exact - this
        /// keeps the proven projection formula and only picks a sensible
        /// starting zoom for short, wide viewer panes.
        /// </summary>
        private double ComputeFitZoom(int w, int h)
        {
            if (w < 16 || h < 16) return DefaultZoom;

            double savedYaw = _yaw, savedPitch = _pitch, savedZoom = _zoom;
            double savedPanX = _panX, savedPanY = _panY;
            _yaw = DefaultYaw;
            _pitch = DefaultPitch;
            _zoom = 1.0;
            _panX = 0;
            _panY = 0;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double zmin, zmax;
            HeightRange(out zmin, out zmax);
            for (int i = 0; i < 8; i++)
            {
                double x = (i & 1) == 0 ? -1.2 : 1.2;
                double y = (i & 2) == 0 ? -0.6 : 0.6;
                double z = (i & 4) == 0 ? zmin : zmax;
                PointF p; float iz;
                Project(x, y, z, w, h, out p, out iz);
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            _yaw = savedYaw;
            _pitch = savedPitch;
            _zoom = savedZoom;
            _panX = savedPanX;
            _panY = savedPanY;

            double extentX = Math.Max(1.0, maxX - minX);
            double extentY = Math.Max(1.0, maxY - minY);
            // Leave room for the two caption lines and the X tick labels.
            double usableW = Math.Max(40.0, w - 30.0);
            double usableH = Math.Max(30.0, h - 46.0);
            double fit = Math.Min(usableW / extentX, usableH / extentY) * 0.97;
            return Clamp(fit, 0.35, 3.5);
        }

        private void HeightRange(out double zmin, out double zmax)
        {
            zmin = double.MaxValue;
            zmax = double.MinValue;
            if (_z != null)
            {
                for (int r = 0; r < _rows; r++)
                {
                    for (int c = 0; c < _cols; c++)
                    {
                        if (_z[r, c] < zmin) zmin = _z[r, c];
                        if (_z[r, c] > zmax) zmax = _z[r, c];
                    }
                }
            }
            if (zmax < zmin) { zmin = -0.45; zmax = 0.45; }
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            SyncPalette();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // Switching back to 3D re-reads the palette and repaints on demand.
            SyncPalette();
            if (Visible) Invalidate();
        }

        // ------------------------------------------------------------------
        // projection
        // ------------------------------------------------------------------

        private void Project(double x, double y, double z,
                             int w, int h,
                             out PointF screen, out float invDepth)
        {
            double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw);
            double x1 = x * cy - y * sy;
            double y1 = x * sy + y * cy;

            double cp = Math.Cos(_pitch), sp = Math.Sin(_pitch);
            // Tilt the model so that +Z rises on screen (camera above the field).
            double y2 = y1 * cp + z * sp;
            double z2 = -y1 * sp + z * cp;

            double d = CamDist - z2;
            if (d < 0.2) d = 0.2;
            double k = Math.Min(w, h) * BaseK * _zoom;
            double s = k / d;
            screen = new PointF((float)(w * 0.5 + _panX + x1 * s),
                                (float)(h * 0.5 + _panY - y2 * s));
            invDepth = (float)(1.0 / d);
        }

        private Vec3 Rotate(double x, double y, double z)
        {
            double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw);
            double x1 = x * cy - y * sy;
            double y1 = x * sy + y * cy;
            double cp = Math.Cos(_pitch), sp = Math.Sin(_pitch);
            return new Vec3(x1, y1 * cp + z * sp, -y1 * sp + z * cp);
        }

        // ------------------------------------------------------------------
        // painting
        // ------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            long t0 = Stopwatch.GetTimestamp();
            SyncPalette();

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 16 || h < 16) return;
            try
            {
                RenderScene(g, w, h, t0);
            }
            catch (Exception ex)
            {
                // A view must never take the app down; make the failure
                // discoverable instead of leaving a silently blank pane.
                LogDebug("paint-ex: " + ex);
                Console.Error.WriteLine("Reloaded 3D surface: " + ex);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Wine does not always turn the layout-driven resize into a paint
            // for a UserPaint control, so ask for one explicitly.  The fit zoom
            // follows the new area until the user zooms by hand.
            _fitZoom = ComputeFitZoom(ClientSize.Width, ClientSize.Height);
            if (!_userZoomed) _zoom = _fitZoom;
            LogDebug("resize w=" + Width + " h=" + Height + " zoom=" + _zoom.ToString("0.00"));
            Invalidate();
        }

        private void RenderScene(Graphics g, int w, int h, long t0)
        {
            EnsureBuffers(w, h);
            for (int i = 0; i < _pixels.Length; i++)
            {
                _pixels[i] = _bgArgb;
                _zbuf[i] = 0f;                    // 1/depth, 0 = infinitely far
            }

            // Real value range of THIS map (also what the Z labels show).
            double vmin, vmax;
            BuildHeights(out vmin, out vmax);

            // Project every grid node once.
            PointF[,] pts = new PointF[_rows, _cols];
            float[,] inv = new float[_rows, _cols];
            Vec3[,] world = new Vec3[_rows, _cols];
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    double x = (U(c) - 0.5) * 2.4;
                    double y = (0.5 - V(r)) * 1.2;
                    PointF p; float iz;
                    Project(x, y, _z[r, c], w, h, out p, out iz);
                    pts[r, c] = p;
                    inv[r, c] = iz;
                    world[r, c] = Rotate(x, y, _z[r, c]);
                }
            }

            // Rasterise the surface into the colour + depth buffers.
            for (int r = 0; r < _rows - 1; r++)
            {
                for (int c = 0; c < _cols - 1; c++)
                {
                    double t = 0.25 * (_values[c, r] + _values[c + 1, r] +
                                        _values[c + 1, r + 1] + _values[c, r + 1]);
                    t = Normalize(t, vmin, vmax);

                    Vec3 n = QuadNormal(world, r, c);
                    double len = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
                    if (len > 1e-9) { n.X /= len; n.Y /= len; n.Z /= len; }
                    // Fixed light from upper-left/front, in camera space.
                    const double lx = -0.35, ly = 0.55, lz = 0.75;
                    double ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
                    double lam = (n.X * lx + n.Y * ly + n.Z * lz) / ll;
                    if (lam < 0) lam = 0;
                    double light = 0.68 + 0.32 * lam;

                    Color fill = Shade(Ramp(t), light);
                    int fillArgb = fill.ToArgb();
                    // Wireframe: a crisp black grid over the shaded surface (the
                    // original Nevron look), the same in both themes - the
                    // surface itself is always the value ramp.
                    int wireArgb = Color.FromArgb(10, 10, 10).ToArgb();

                    PointF a = pts[r, c], b = pts[r, c + 1];
                    PointF cc = pts[r + 1, c + 1], d = pts[r + 1, c];
                    RasterTri(a, b, cc, inv[r, c], inv[r, c + 1], inv[r + 1, c + 1], fillArgb);
                    RasterTri(a, cc, d, inv[r, c], inv[r + 1, c + 1], inv[r + 1, c], fillArgb);

                    RasterLine3D(a, b, inv[r, c], inv[r, c + 1], wireArgb);
                    RasterLine3D(b, cc, inv[r, c + 1], inv[r + 1, c + 1], wireArgb);
                    RasterLine3D(cc, d, inv[r + 1, c + 1], inv[r + 1, c], wireArgb);
                    RasterLine3D(d, a, inv[r + 1, c], inv[r, c], wireArgb);
                }
            }

            // Frame + real-data axis labels (projected so they follow the view).
            PointF frameOrigin, frameX, frameY, frameZ;
            DrawFrame(w, h, out frameOrigin, out frameX, out frameY, out frameZ);

            // A one-row / one-column map (a Kennlinie) has no quads; draw it as
            // a depth-tested 3D curve so the view is never blank.
            if (_rows < 2 || _cols < 2)
                DrawCurve(pts, inv, vmin, vmax);

            // Blit the software buffer, then GDI+ text on top.
            BitmapData bd = _bmp.LockBits(new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            Marshal.Copy(_pixels, 0, bd.Scan0, _pixels.Length);
            _bmp.UnlockBits(bd);
            g.DrawImageUnscaled(_bmp, 0, 0);

            DrawAxisLabels(g, w, h, frameOrigin, frameX, frameY, frameZ, vmin, vmax);

            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            _lastPaintMs = _lastPaintMs == 0 ? ms : _lastPaintMs * 0.9 + ms * 0.1;

            DrawHud(g, w, h, vmin, vmax);

            _frames++;
            double now = _clock.Elapsed.TotalSeconds;
            if (now - _fpsWindowStart >= 0.5)
            {
                _fps = _frames / (now - _fpsWindowStart);
                _frames = 0;
                _fpsWindowStart = now;
                LogMeasurement();
            }
        }

        /// <summary>Grid column index -> normalised 0..1 (a 1-wide grid sits centred).</summary>
        private double U(int c)
        {
            return _cols > 1 ? c / (double)(_cols - 1) : 0.5;
        }

        /// <summary>Grid row index -> normalised 0..1 (a 1-row grid sits centred).</summary>
        private double V(int r)
        {
            return _rows > 1 ? r / (double)(_rows - 1) : 0.5;
        }

        /// <summary>
        /// Normalizes the shared value grid into world heights.  Recomputed per
        /// paint so edits in the cell grid and range changes are reflected
        /// without any cross-control notification.
        /// </summary>
        private void BuildHeights(out double vmin, out double vmax)
        {
            if (_z == null || _z.GetLength(0) != _rows || _z.GetLength(1) != _cols)
                _z = new double[_rows, _cols];

            vmin = double.MaxValue;
            vmax = double.MinValue;
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    double v = _values[c, r];
                    if (v < vmin) vmin = v;
                    if (v > vmax) vmax = v;
                }
            }
            if (vmax < vmin) { vmin = 0; vmax = 1; }

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    double t = Normalize(_values[c, r], vmin, vmax);
                    _z[r, c] = (t - 0.45) * 0.9;
                }
            }
            // A flat map (vmax == vmin) lands at mid height through
            // Normalize()'s 0.5 fallback, so the surface stays a flat sheet.
        }

        private static double Normalize(double v, double min, double max)
        {
            double span = max - min;
            if (span <= 1e-12) return 0.5;      // flat map: mid height, uniform colour
            double t = (v - min) / span;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return t;
        }

        private void EnsureBuffers(int w, int h)
        {
            if (_bmp != null && _bw == w && _bh == h) return;
            if (_bmp != null) _bmp.Dispose();
            _bw = w; _bh = h;
            _bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            _pixels = new int[w * h];
            _zbuf = new float[w * h];
        }

        // ---- colour ramp + shading --------------------------------------

        /// <summary>green -> yellow -> orange -> red (the 2D heat map's ramp family).</summary>
        private static Color Ramp(double t)
        {
            Color[] stops =
            {
                Color.FromArgb(24, 148, 68),
                Color.FromArgb(236, 214, 48),
                Color.FromArgb(248, 140, 24),
                Color.FromArgb(202, 28, 28)
            };
            double[] at = { 0.0, 0.38, 0.68, 1.0 };
            if (t <= 0.0) return stops[0];
            if (t >= 1.0) return stops[3];
            for (int i = 0; i < 3; i++)
            {
                if (t <= at[i + 1])
                {
                    double f = (t - at[i]) / (at[i + 1] - at[i]);
                    return Color.FromArgb(
                        (int)(stops[i].R + (stops[i + 1].R - stops[i].R) * f),
                        (int)(stops[i].G + (stops[i + 1].G - stops[i].G) * f),
                        (int)(stops[i].B + (stops[i + 1].B - stops[i].B) * f));
                }
            }
            return stops[3];
        }

        private static Color Shade(Color c, double f)
        {
            int r = (int)(c.R * f), gg = (int)(c.G * f), b = (int)(c.B * f);
            if (r > 255) r = 255; if (r < 0) r = 0;
            if (gg > 255) gg = 255; if (gg < 0) gg = 0;
            if (b > 255) b = 255; if (b < 0) b = 0;
            return Color.FromArgb(r, gg, b);
        }

        private static Color Blend(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        // ---- software rasteriser (spike core, unchanged) ----------------

        private void RasterTri(PointF p0, PointF p1, PointF p2,
                               float iz0, float iz1, float iz2, int argb)
        {
            double x0 = p0.X, y0 = p0.Y;
            double x1 = p1.X, y1 = p1.Y;
            double x2 = p2.X, y2 = p2.Y;

            double area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
            if (Math.Abs(area) < 1e-7) return;
            bool pos = area > 0;

            int minX = (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)));
            int maxX = (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2)));
            int minY = (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)));
            int maxY = (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2)));
            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX > _bw - 1) maxX = _bw - 1;
            if (maxY > _bh - 1) maxY = _bh - 1;

            for (int y = minY; y <= maxY; y++)
            {
                double py = y + 0.5;
                int row = y * _bw;
                for (int x = minX; x <= maxX; x++)
                {
                    double px = x + 0.5;
                    double w0 = (x2 - x1) * (py - y1) - (y2 - y1) * (px - x1);
                    double w1 = (x0 - x2) * (py - y2) - (y0 - y2) * (px - x2);
                    double w2 = (x1 - x0) * (py - y0) - (y1 - y0) * (px - x0);
                    if (pos)
                    {
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    }
                    else
                    {
                        if (w0 > 0 || w1 > 0 || w2 > 0) continue;
                    }
                    double iz = (w0 * iz0 + w1 * iz1 + w2 * iz2) / area;
                    int idx = row + x;
                    if (iz > _zbuf[idx])
                    {
                        _zbuf[idx] = (float)iz;
                        _pixels[idx] = argb;
                    }
                }
            }
        }

        private void RasterLine3D(PointF a, PointF b, float iza, float izb, int argb)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
            if (steps < 1) steps = 1;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                int x = (int)Math.Round(a.X + dx * t);
                int y = (int)Math.Round(a.Y + dy * t);
                if (x < 0 || y < 0 || x >= _bw || y >= _bh) continue;
                float iz = (float)(iza * (1.0 - t) + izb * t);
                int idx = y * _bw + x;
                // Tolerance: an edge lies at exactly the fill's depth.
                if (iz + 2e-4f >= _zbuf[idx]) _pixels[idx] = argb;
            }
        }

        private Vec3 QuadNormal(Vec3[,] world, int r, int c)
        {
            Vec3 p00 = world[r, c];
            Vec3 p10 = world[r, c + 1];
            Vec3 p01 = world[r + 1, c];
            Vec3 e1 = new Vec3(p10.X - p00.X, p10.Y - p00.Y, p10.Z - p00.Z);
            Vec3 e2 = new Vec3(p01.X - p00.X, p01.Y - p00.Y, p01.Z - p00.Z);
            return Cross(e2, e1);
        }

        private static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(a.Y * b.Z - a.Z * b.Y,
                            a.Z * b.X - a.X * b.Z,
                            a.X * b.Y - a.Y * b.X);
        }

        // ---- frame + labels ---------------------------------------------

        /// <summary>
        /// One-row / one-column maps (Kennlinien) have no quads: draw the cell
        /// values as a depth-tested 3D polyline with square node markers, so the
        /// viewer shows the curve instead of an empty frame.
        /// </summary>
        private void DrawCurve(PointF[,] pts, float[,] inv, double vmin, double vmax)
        {
            int count = _cols > 1 ? _cols : _rows;
            for (int i = 0; i < count; i++)
            {
                int c = _cols > 1 ? i : 0;
                int r = _cols > 1 ? 0 : i;
                double t = Normalize(_values[c, r], vmin, vmax);
                int argb = Shade(Ramp(t), 0.95).ToArgb();

                // Node marker: 3 x 3 square, depth tested.
                int px = (int)Math.Round(pts[r, c].X);
                int py = (int)Math.Round(pts[r, c].Y);
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int x = px + ox, y = py + oy;
                        if (x < 0 || y < 0 || x >= _bw || y >= _bh) continue;
                        int idx = y * _bw + x;
                        if (inv[r, c] + 2e-4f >= _zbuf[idx]) _pixels[idx] = argb;
                    }
                }

                if (i == 0) continue;
                int c0 = _cols > 1 ? i - 1 : 0;
                int r0 = _cols > 1 ? 0 : i - 1;
                PointF a = pts[r0, c0], b = pts[r, c];
                RasterLine3D(a, b, inv[r0, c0], inv[r, c], argb);
                RasterLine3D(new PointF(a.X, a.Y + 1f), new PointF(b.X, b.Y + 1f),
                             inv[r0, c0], inv[r, c], argb);
            }
        }

        /// <summary>
        /// Draws the three frame axes under the surface (depth-tested, so they
        /// disappear behind ridges) and returns their projected anchors for the
        /// tick labels.
        /// </summary>
        private void DrawFrame(int w, int h,
                               out PointF origin, out PointF xTip, out PointF yTip, out PointF zTip)
        {
            double zmin = double.MaxValue;
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _cols; c++)
                    if (_z[r, c] < zmin) zmin = _z[r, c];
            zmin -= 0.06;

            float io, ix, iy, iz;
            Project(-1.24, -0.63, zmin, w, h, out origin, out io);
            Project(1.24, -0.63, zmin, w, h, out xTip, out ix);
            Project(-1.24, 0.63, zmin, w, h, out yTip, out iy);
            Project(-1.24, -0.63, zmin + 1.02, w, h, out zTip, out iz);

            int col = _frame.ToArgb();
            RasterLine3D(origin, xTip, io, ix, col);
            RasterLine3D(origin, yTip, io, iy, col);
            RasterLine3D(origin, zTip, io, iz, col);
        }

        /// <summary>
        /// Axis tick labels from the REAL axis arrays: three ticks per axis at
        /// the first / middle / last cell, plus the value range on Z.  Titles
        /// (with the map unit) live in the HUD line, the numbers sit on the axes.
        /// </summary>
        private void DrawAxisLabels(Graphics g, int w, int h,
                                    PointF origin, PointF xTip, PointF yTip, PointF zTip,
                                    double vmin, double vmax)
        {
            double zbase = FrameBase();
            using (SolidBrush ink = new SolidBrush(_text))
            using (SolidBrush muted = new SolidBrush(_muted))
            {
                // X: real x axis values along the front edge.  The outermost
                // labels are pushed inward so they never run off the pane.
                int[] cols = TickIndices(_cols);
                for (int i = 0; i < cols.Length; i++)
                {
                    PointF p; float iz;
                    Project((U(cols[i]) - 0.5) * 2.4, -0.63, zbase, w, h, out p, out iz);
                    string label = AxisLabel(_xValues, cols[i]);
                    SizeF size = g.MeasureString(label, _tickFont);
                    float x = p.X - size.Width * 0.5f;
                    if (i == cols.Length - 1) x = p.X - size.Width + 2f;   // ends at the corner
                    DrawText(g, ink, label, x, p.Y + 4f, w, h);
                }

                // Y: real y axis values along the left edge (row 0 at the back).
                int[] rows = TickIndices(_rows);
                for (int i = 0; i < rows.Length; i++)
                {
                    PointF p; float iz;
                    Project(-1.24, (0.5 - V(rows[i])) * 1.2, zbase, w, h, out p, out iz);
                    string label = AxisLabel(_yValues, rows[i]);
                    SizeF size = g.MeasureString(label, _tickFont);
                    DrawText(g, ink, label, p.X - size.Width - 5f, p.Y - size.Height * 0.5f, w, h);
                }

                // Z: value range at the vertical frame axis.
                DrawText(g, muted, FormatValue(vmin), zTip.X + 5f, zTip.Y - 2f, w, h);
                DrawText(g, muted, FormatValue(vmax), zTip.X + 5f,
                         zTip.Y - (float)((zTip.Y - origin.Y) * 0.5) - 12f, w, h);
            }
        }

        /// <summary>World Z of the frame base (same expression DrawFrame uses).</summary>
        private double FrameBase()
        {
            double zmin = double.MaxValue;
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _cols; c++)
                    if (_z[r, c] < zmin) zmin = _z[r, c];
            return zmin - 0.06;
        }

        private void DrawText(Graphics g, Brush brush, string text, float x, float y, int w, int h)
        {
            if (x < 2) x = 2;
            if (y < 2) y = 2;
            if (x > w - 24) x = w - 24;
            if (y > h - 16) y = h - 16;
            g.DrawString(text, _tickFont, brush, x, y);
        }

        /// <summary>Compact caption: dimensions, value range and axis names.</summary>
        private void DrawHud(Graphics g, int w, int h, double vmin, double vmax)
        {
            string caption = string.Format(CultureInfo.InvariantCulture,
                "{0} x {1} Zellen   ·   Werte {2} .. {3}",
                _cols, _rows, FormatValue(vmin), FormatValue(vmax));

            string axes = string.Format(CultureInfo.InvariantCulture,
                "X: {0}     Y: {1}", AxisCaption(_xTitle, "X-Achse"), AxisCaption(_yTitle, "Y-Achse"));

            using (SolidBrush ink = new SolidBrush(_text))
            using (SolidBrush muted = new SolidBrush(_muted))
            {
                g.DrawString(caption, _captionFont, ink, 12, 8);
                g.DrawString(axes, _hudFont, muted, 12, 24);
            }
        }

        /// <summary>First / middle / last cell index, without duplicates on tiny axes.</summary>
        private static int[] TickIndices(int count)
        {
            if (count <= 1) return new int[] { 0 };
            if (count == 2) return new int[] { 0, 1 };
            return new int[] { 0, (count - 1) / 2, count - 1 };
        }

        /// <summary>Caption for the HUD: the map's axis title, never a bare "X".</summary>
        private static string AxisCaption(string title, string fallback)
        {
            if (string.IsNullOrEmpty(title)) return fallback;
            if (string.Equals(title, "X", StringComparison.Ordinal) ||
                string.Equals(title, "Y", StringComparison.Ordinal))
                return fallback;
            return title;
        }

        private static string FormatValue(double v)
        {
            if (Math.Abs(v) < 0.0005) return "0";
            if (Math.Abs(v) >= 1000) return v.ToString("0", CultureInfo.CurrentCulture);
            return v.ToString("0.##", CultureInfo.CurrentCulture);
        }

        private static string AxisLabel(double[] values, int index)
        {
            if (values == null || values.Length == 0) return "?";
            if (index < 0) index = 0;
            if (index >= values.Length) index = values.Length - 1;
            return FormatValue(values[index]);
        }

        private void LogMeasurement()
        {
            if (_fpsLogPath == null) return;
            try
            {
                File.AppendAllText(_fpsLogPath, string.Format(CultureInfo.InvariantCulture,
                    "{0:HH:mm:ss.fff}\tfps={1:0.0}\tpaint={2:0.00}ms\tgrid={3}x{4}\r\n",
                    DateTime.Now, _fps, _lastPaintMs, _cols, _rows));
            }
            catch
            {
                // Diagnostics only - never let logging break the viewer.
            }
        }

        private void LogDebug(string message)
        {
            if (_fpsLogPath == null) return;
            try
            {
                File.AppendAllText(_fpsLogPath, message + "\r\n");
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------
        // input: drag = rotate, wheel = zoom
        // ------------------------------------------------------------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _panning = false;
                _lastMouse = e.Location;
                Capture = true;
                Cursor = Cursors.Hand;
            }
            else if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                _dragging = true;
                _panning = true;
                _lastMouse = e.Location;
                Capture = true;
                Cursor = Cursors.Hand;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            int dx = e.X - _lastMouse.X;
            int dy = e.Y - _lastMouse.Y;
            _lastMouse = e.Location;
            if (_panning)
            {
                _panX += dx;
                _panY += dy;
            }
            else
            {
                _yaw += dx * 0.008;
                _pitch = Clamp(_pitch + dy * 0.008, 0.10, 1.45);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging &&
                (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right ||
                 e.Button == MouseButtons.Middle))
            {
                _dragging = false;
                _panning = false;
                Capture = false;
                Cursor = Cursors.Cross;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            ResetView();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomBy(e.Delta);
        }

        private void ZoomBy(int delta)
        {
            Point client = PointToClient(Control.MousePosition);
            double factor = Math.Pow(1.12, delta / 120.0);
            ZoomAt(client, factor);
        }

        /// <summary>
        /// Zooms keeping the world point under <paramref name="client"/> fixed,
        /// so the wheel zooms on the cursor, not on the centre of the surface.
        /// </summary>
        private void ZoomAt(Point client, double factor)
        {
            double w = ClientSize.Width, h = ClientSize.Height;
            double ox = client.X - w * 0.5 - _panX;
            double oy = client.Y - h * 0.5 - _panY;
            _zoom = Clamp(_zoom * factor, 0.35, 3.5);
            _panX = client.X - w * 0.5 - ox * factor;
            _panY = client.Y - h * 0.5 - oy * factor;
            _userZoomed = true;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Home must reach OnKeyDown instead of walking the dialog.
            if (keyData == Keys.Home) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Home)
            {
                ResetView();
                e.Handled = true;
            }
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_host != null)
                {
                    _host.BackColorChanged -= HostBackColorChanged;
                    _host = null;
                }
                if (_router != null)
                {
                    Application.RemoveMessageFilter(_router);
                    _router = null;
                }
                if (_bmp != null) _bmp.Dispose();
                _tickFont.Dispose();
                _hudFont.Dispose();
                _captionFont.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------
        // wheel routing
        // ------------------------------------------------------------------

        /// <summary>
        /// A plain Control does not receive WM_MOUSEWHEEL unless it holds
        /// focus, so after opening a map with the keyboard the first wheel turn
        /// would go to the map tree.  This thread-wide filter forwards wheels
        /// that happen over the surface, without stealing focus from anything.
        /// </summary>
        private sealed class WheelRouter : IMessageFilter
        {
            private const int WmMouseWheel = 0x020A;
            private readonly SurfaceView _owner;

            private WheelRouter(SurfaceView owner) { _owner = owner; }

            public static WheelRouter Install(SurfaceView owner)
            {
                WheelRouter router = new WheelRouter(owner);
                Application.AddMessageFilter(router);
                return router;
            }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WmMouseWheel) return false;
                if (_owner == null || _owner.IsDisposed || !_owner.IsHandleCreated) return false;
                if (!_owner.Visible) return false;

                Point cursor = Control.MousePosition;
                Point client = _owner.PointToClient(cursor);
                if (client.X < 0 || client.Y < 0 ||
                    client.X >= _owner.ClientSize.Width || client.Y >= _owner.ClientSize.Height)
                    return false;

                // HIWORD of wParam is the signed wheel delta.
                int delta = (short)((long)m.WParam >> 16);
                if (delta == 0) return false;
                _owner.ZoomBy(delta);
                return true;
            }
        }
    }
}
