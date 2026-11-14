//---------------------------------------------------------------------------
//
// Core/Enums.cs
//
// Enums shared between the UI and the parsers.  They lived in frmMain.cs,
// which pulled the whole WinForms/DevExpress surface into every file that
// only needs a file-type constant.  Keeping them here lets the parsers and
// tools compile without any UI dependency (the first step of the
// DevExpress-free rewrite).
//
//---------------------------------------------------------------------------

namespace VAGSuite
{
    public enum GearboxType : int
    {
        Automatic,
        Manual,
        FourWheelDrive
    }

    public enum EDCFileType : int
    {
        EDC15P,
        EDC15P6, // different map structure
        EDC15V,
        EDC15M,
        EDC15C,
        EDC16,
        EDC17,  // 512Kb/2048Kb
        MSA6,
        MSA11,
        MSA12,
        MSA15,
        Unknown
    }

    public enum EngineType : int
    {
        cc1200,
        cc1400,
        cc1600,
        cc1900,
        cc2500
    }

    public enum ImportFileType : int
    {
        XML,
        A2L,
        CSV,
        AS2,
        Damos
    }

    // Axis selector used by the parsers' axis-validation helpers.  It used
    // to be nested in MapViewerEx (a DevExpress/Nevron WinForms control),
    // which forced the pure parsing code to reference the whole UI control.
    // The values are unchanged and MapViewerEx.AxisIdent stays as-is for
    // the UI code; parsers now use this top-level enum.
    public enum AxisIdent : int
    {
        X_Axis = 0,
        Y_Axis = 1,
        Z_Axis = 2
    }
}
