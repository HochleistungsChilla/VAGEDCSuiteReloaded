using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VAGSuite
{
    
    public enum ChecksumResult : int
    {
        ChecksumOK,
        ChecksumFail,
        ChecksumTypeError,
        ChecksumTypeUnknown
    }
    
    public enum ChecksumType : int
    {
        VAG_EDC15P_V41,
        VAG_EDC15P_V41V2,
        VAG_EDC15P_V41_2002,
        VAG_EDC15VM_V41,
        VAG_EDC15VM_V41V2,
        VAG_EDC15VM_V41_2002,
        Unknown = 6,
        // 256 KB EDC15V-5, explicit value so Unknown keeps its old number
        VAG_EDC15VM_256K = 7
    }

    public class ChecksumResultDetails
    {
        private bool _calculationOk = false;

        public bool CalculationOk
        {
            get { return _calculationOk; }
            set { _calculationOk = value; }
        }

        private ChecksumResult _calculationResult = ChecksumResult.ChecksumTypeUnknown;

        public ChecksumResult CalculationResult
        {
            get { return _calculationResult; }
            set { _calculationResult = value; }
        }
        private ChecksumType _typeResult = ChecksumType.Unknown;

        public ChecksumType TypeResult
        {
            get { return _typeResult; }
            set { _typeResult = value; }
        }
        private int _numberChecksumsTotal = 0;

        public int NumberChecksumsTotal
        {
            get { return _numberChecksumsTotal; }
            set { _numberChecksumsTotal = value; }
        }
        private int _numberChecksumsOk = 0;

        public int NumberChecksumsOk
        {
            get { return _numberChecksumsOk; }
            set { _numberChecksumsOk = value; }
        }
        private int _numberChecksumsFail = 0;

        public int NumberChecksumsFail
        {
            get { return _numberChecksumsFail; }
            set { _numberChecksumsFail = value; }
        }
    }
}
