# Label verification EDC15V-5_SG1623.labels

Generated with `tools/verify_map_labels.py` against `C# symbol table sym_msa15_base.tsv (without label file)`.

## Coverage per code block

| Block | 3-D maps | 1-D curves | Single values |
|---|---|---|---|
| codeblock 2 (+0x00000) | 62/62 | 69/69 | 3637/3856 |
| codeblock 3 (+0x08000) | 62/62 | 69/69 | 3637/3856 |
| codeblock 1 (+0x38000) | 62/62 | 69/69 | 3635/3856 |

Renamed/created: 11251, heuristic name kept: 710, skipped: 710, rejected: 0.

Skipped are addresses that lie in an object detected by the heuristic (axis values or data); creating a second symbol there would overlap the existing one. 65 curves with 8-bit storage width are not included in the denominator (the loader skips them).

## The 62 3-D maps (block 2)

| addr | dims | before | after |
|---|---|---|---|
| 0x0128C | 4x4 | (unnamed) | air loss moment-Map At 100% Last |
| 0x01312 | 10x10 | (unnamed) | AG4 - withdrawal of amounts of - Map ( re switch ) |
| 0x0140A | 10x10 | (unnamed) | AG4 - withdrawal of amounts of - Map ( Upshift ) |
| 0x0152A | 8x8 | (unnamed) | Calculation of specific fuel consumption bfsi = f(N, M_fuel) |
| 0x01608 | 8x8 | MAF correction by temperature | EXT friction torque map Md = f(N, T_water) |
| 0x016C8 | 12x16 | (unnamed) | Inverse Driver's wish |
| 0x019DC | 16x8 | Driver wish | Driver wish |
| 0x01D80 | 16x13 | EGR | EGR |
| 0x01FC2 | 8x8 | (unnamed) | ARF height correction, ARF = arwPAKORKL * arwPAKORKF |
| 0x02056 | 2x4 | (unnamed) | EGR multipl. setpoint value correction Factor = f(N, T_air) |
| 0x02086 | 6x6 | (unnamed) | EGR additive setpoint value correction dM_air = f(N, T_water) |
| 0x0218E | 16x13 | (unnamed) | Permitted Deviation Basic map |
| 0x02370 | 16x13 | IQ by MAP limiter | Factor Permitted Deviation correction characteristic map |
| 0x02646 | 2x2 | (unnamed) | MLB airflow correction map Factor = f(T_air, P_atm) |
| 0x027EA | 10x10 | (unnamed) | Basic characteristic exhaust gas recirculation control |
| 0x029E6 | 10x10 | (unnamed) | TV for Actuator 2 At switched off Regulation |
| 0x03AE0 | 8x9 | Start IQ | Start IQ |
| 0x03BFE | 11x5 | (unnamed) | BEG air temp. dependent full load increase M_fuel = f(N, T_water) |
| 0x03E52 | 3x19 | Torque limiter | Torque limiter |
| 0x03F34 | 16x12 | Smoke limiter | Smoke limiter |
| 0x04112 | 16x10 | (unnamed) | MAF Temp Correction |
| 0x04294 | 16x13 | IQ by MAP limiter | Smoke Limiter by MAP, M_fuel = f(N, P_aircorr) |
| 0x04470 | 16x10 | (unnamed) | Smoke limiting, corrected boost pressure(MAP) |
| 0x04638 | 10x10 | (unnamed) | BEG overheating protection map M_fuel = (T_oil, V) |
| 0x04730 | 10x10 | (unnamed) | BEG full load reduction map Factor = f(P_atm, N) |
| 0x04818 | 6x6 | (unnamed) | BEG overheating protection map M_fuel = (T_water, V) |
| 0x048AC | 10x10 | (unnamed) | METK T_K-abh. Volume Correction dMETK = f(N, M_E) |
| 0x049A6 | 9x8 | (unnamed) | Fuel temp. dependent fuel qu. correction dME/TK = f(N, M_fuel) |
| 0x04F28 | 16x17 | N146 Pump voltage map | N146 Pump voltage map |
| 0x05476 | 16x10 | Boost target map | Boost target map |
| 0x055F2 | 16x10 | Boost target map | Boost target map |
| 0x0575E | 9x9 | (unnamed) | correction characteristic map = f( P_ATM, M_E ), Ueberdruckber. |
| 0x05850 | 10x10 | Boost limit map | Boost limit map |
| 0x059B2 | 16x13 | N75 duty cycle | N75 duty cycle |
| 0x05B94 | 16x13 | N75 duty cycle | N75 duty cycle |
| 0x05D60 | 9x9 | (unnamed) | LDR correction characteristic map = f( P_ATM, M_E ) f. control |
| 0x05E94 | 2x5 | (unnamed) | Pre-glow time map t_glow = f(U_batt, T_water) |
| 0x05EE4 | 2x2 | (unnamed) | Relay control for glow plug control Relay = f(N, M_fuel) |
| 0x06050 | 2x2 | (unnamed) | Correction for to differential temperature |
| 0x060EC | 2x2 | (unnamed) | BDP memory factor target differential GF = f(N, M_EAKT) |
| 0x06104 | 2x2 | (unnamed) | BDP memory factor current temp. steig. ATF1 GF = f(N, M_EAKT) |
| 0x0611C | 2x2 | (unnamed) | BDP memory factor current temp. case. ATF1 GF = f(N, M_EAKT) |
| 0x06134 | 2x2 | (unnamed) | BDP memory factor current temp. steig. ATF2 GF = f(N, M_EAKT) |
| 0x0614C | 2x2 | (unnamed) | BDP memory factor current temp. case. ATF2 GF = f(N, M_EAKT) |
| 0x06164 | 2x2 | (unnamed) | BDP-amounts Map dosage Tropfenfrequenz |
| 0x0617C | 2x2 | (unnamed) | BDP-amounts Correction - Map dosage |
| 0x06194 | 2x2 | (unnamed) | to differential temperature for Fold-Monitoring |
| 0x06268 | 14x8 | (unnamed) | Calculation the fan control time t = f(WTF, VB) |
| 0x063B4 | 11x5 | (unnamed) | Tastverhaeltnis_Kennfeld |
| 0x06E50 | 9x9 | (unnamed) | SBR correction map 1 Factor = f(T_water, M_fuel) |
| 0x06F52 | 10x10 | (unnamed) | SBR correction map 1 Start of inj. = f(N, M_fuel) |
| 0x07062 | 14x7 | (unnamed) | SBR correction map 2 Start of inj. = f(N, M_fuel) |
| 0x07176 | 14x7 | (unnamed) | SBR correction map 3 Start of inj. = f(N, M_fuel) |
| 0x0730C | 13x7 | Boost limiter | Start Of Injection Limiter |
| 0x07404 | 16x13 | Start of injection | Start of injection |
| 0x075B8 | 3x3 | (unnamed) | SBR timing control at starting, duty cycle = f(N, T_water) |
| 0x0763E | 3x3 | (unnamed) | SBR timing control map, correct. duty cycle = f(N, T_water) |
| 0x0768C | 16x10 | IQ to airmass | N108 Advance Valve |
| 0x07826 | 10x8 | (unnamed) | Water temperature corr. for ARF-control |
| 0x078F8 | 10x4 | (unnamed) | pressure correction map exhaust gas recirculation control |
| 0x07974 | 10x8 | (unnamed) | ARF additive Rules-Correction dM_L = f(N, T_W) |
| 0x07A40 | 9x9 | (unnamed) | pressure correction map exhaust gas recirculation Regulation |

## The 69 1-D curves (block 2)

| addr | Points | Axis | Name | Unit |
|---|---|---|---|---|
| 0x011B0 | 2 | 0..15677 | Gate-array answers = f(Gate-array questions) | - |
| 0x012C2 | 9 | 0..6000 | generator load-Torque loss-Characteristic | Nm |
| 0x014EC | 11 | 612..5506 | Drive away torque quantity curve M_fuel = f(N) | mg/Hub |
| 0x01CE0 | 20 | 2231..3181 | Time for calculating water end temperature t_test = f(T_water) | us |
| 0x01F92 | 4 | 600..920 | EGR additive setpoint correction dM_air = f(P_atm) | mg/Hub air |
| 0x02140 | 5 | 200..3000 | Water temperature corr. for ARF = arwTWKORKF * arwMEKORKL | - |
| 0x0254E | 20 | 0..4200 | Quantity threshold for shutdown Lower Hysteresis | mg/Hub |
| 0x025A2 | 20 | 0..4200 | Quantity threshold for shutdown top Hysteresis | mg/Hub |
| 0x025E2 | 10 | 2481..2906 | EGR Delay for shut off after starting Time = f(T_water) | us |
| 0x026FC | 13 | 314..9600 | MAF linearisation, % to mass flow | kg / h air |
| 0x0277A | 10 | 210..2100 | EGR lower hyster. thresh. switching governing/control M_fuel = f(N) | mg/Hub |
| 0x027A6 | 10 | 210..2100 | EGR upper hyster. thresh. switching control/governing M_fuel = f(N) | mg/Hub |
| 0x029AA | 5 | 61536..4000 | EGR converion adjustment value to duty cycle = f(adjust.v.) | % |
| 0x03BA0 | 2 | 2530..2832 | STA start cut-out speed curve N = f(T_water) (offen) | 1/Min |
| 0x045C0 | 6 | 600..1100 | BEG TSB rise ramps-KL positive dM_E = f(P_ATM) | (mg/Hub)/s |
| 0x045DC | 6 | 600..1100 | BEG TSB rise ramps-KL negative dM_E = f(P_ATM) | (mg/Hub)/s |
| 0x045FA | 7 | 2537..4081 | BEG TSB rise ramps-Correction fact = f(T_L) | - |
| 0x04870 | 6 | 441..5355 | Limp Mode Torque Limiter | mg/Hub |
| 0x04BFC | 2 | 2530..2832 | Idle Speed by temp (offen) | 1/Min |
| 0x04C12 | 7 | 2434..3532 | LLR initialisation of I-share M_fuel = f(T_water) | mg/Hub |
| 0x04C82 | 10 | 0..6000 | intake manifold vacuum curve Pmax = f(N) | hPa |
| 0x04CAE | 10 | 0..6000 | intake manifold vacuum healing characteristics Pmax = f(N) | hPa |
| 0x04CE6 | 16 | 210..5000 | UEB overrun mode curve Uspecified = f(N) | mV |
| 0x0529C | 16 | 0..1997 | LDR adaptation the P-Amplification f(consumption) | - |
| 0x052EA | 16 | 0..1997 | LDR adaptation the I-Amplification f(consumption) | - |
| 0x0532E | 16 | 0..5000 | LDR Lower Limit of Actuator TV = f(N) | % |
| 0x05372 | 16 | 0..5000 | LDR upper Limit of Actuator TV = f(N) | % |
| 0x053C8 | 16 | 0..1997 | LDR adaptation the D Amplification f(consumption) | - |
| 0x0540C | 16 | 0..1997 | LDR adaptation of DT1 Gedaechtnisfaktors f(consumption) | - |
| 0x05436 | 2 | 612..1012 | ATM pressure difference correction curve dP_atm = f(N) | hPa |
| 0x05812 | 7 | 2537..4081 | LDR Setpoint adjustment(Overprint) fact = f(T_L) | - |
| 0x05930 | 10 | 0..4500 | LDR Threshold for Regulation Turn off | mg/Hub |
| 0x0595C | 10 | 0..4500 | LDR Threshold for Regulation Turn on | mg/Hub |
| 0x05E14 | 7 | 2537..4081 | LDR Correction fact = f(T_L) for control | - |
| 0x05E3A | 10 | 2481..2906 | LDR shutdown after Cold start = f(T_W) | us |
| 0x05E76 | 4 | 2482..2929 | Post-glow time curve t_glow = f(T_water) | us |
| 0x05F0E | 10 | 2331..4081 | Lufttemperaturabh. water swellable value | degree C |
| 0x05F5A | 6 | 525..5355 | speed-. staging threshold of generator load TV | % |
| 0x05FAE | 6 | 525..5355 | speed-. Shutdown of generator load TV | % |
| 0x06060 | 2 | 2231..2731 | bounce for defective catalyst | us |
| 0x0606C | 2 | 655..1309 | ATF1 - Linearization T_X = f(U) | degree C |
| 0x06078 | 2 | 10476..11130 | ATF2 - Linearization T_X = f(U) | degree C |
| 0x0648C | 25 | 0..6020 | FGG_KT stretch factor-Characteristic Line f(high level duration) | Imp/m |
| 0x0652C | 6 | 2381..3231 | LLR T_W-dependent Idle speed after Start N =f(T_W) | 1/Min |
| 0x06802 | 18 | 0..5355 | MVS cycle duration Characteristic | us |
| 0x0686E | 2 | 442..736 | EST U-batt correction for EPW control factor = f(U_batt) | - |
| 0x0687A | 2 | 0..10000 | EST current-controlled output stage (LDS) I_specified = f(duty cycle) | mA |
| 0x06A5E | 18 | 92..962 | Coolant Temp Sensor Linearisation | degree C |
| 0x06AA6 | 2 | 0..1023 | Atmosph. press. sensor - linearisation P_atm = f(U_ADC) | hPa |
| 0x06AD2 | 18 | 94..962 | IAT Sensor Linearisation | degree C |
| 0x06B2A | 10 | 157..951 | Fuel Temp Sensor Linearisation | degree C |
| 0x06BA2 | 18 | 94..962 | Intake manifold air temp. sensor - linearisation T_air = f(U_ADC) | degree C |
| 0x06BCE | 2 | 164..249 | NOX1 - Linearization T_X = f(U) | degree C |
| 0x06BFA | 2 | 164..249 | NOX2 - Linearization T_X = f(U) | degree C |
| 0x06C26 | 2 | 400..4835 | MAP Sensor Linearisation (offen) | hPa |
| 0x06C3A | 6 | 300..3800 | memory factor f LMM-Filter (HFM5 1ms) GF(1) = f(n) | - |
| 0x06C8A | 32 | 0..10000 | MAF sensor linearisation, voltage to % (offen) | % |
| 0x06CD6 | 4 | 0..5000 | Accel. pedal sensor - linearisation PWG = f(U_ADC) | % |
| 0x06CFE | 2 | 10..20 | Ubatt - linearisation, not active | - |
| 0x06D0A | 2 | 10..20 | Needle movement sensor - linearisation, not active | - |
| 0x06D16 | 2 | 0..653 | Electro-pneum. converter - linearisation I_epc = f(U_ADC) | mA |
| 0x06D26 | 2 | 10..20 | Uref - linearisation, not active | - |
| 0x06D32 | 2 | 2..4 | FGR - linearisation, not active | degree C |
| 0x06F00 | 5 | 750..920 | SBR correction curve 2 Factor = f(P_atm) | - |
| 0x06F18 | 5 | 2881..3381 | SBR correction curve 3 Factor = f(T_air) | - |
| 0x072A8 | 8 | 2431..3331 | SB turn swell-Characteristic | Revolutions |
| 0x072CC | 8 | 2331..2831 | SB Correction characteristic 4 | degree KW |
| 0x077D4 | 2 | 393..687 | SBR control variable correction, duty cycle = f(U_batt) | % |
| 0x077EE | 6 | 1..155 | Digitalwertabh. Outdoor Conditions | degree C |
