namespace Z80core
{
    public class Z80State
    {
        private bool activeINT = false;

        private bool activeNMI = false;

        private bool ffIFF1 = false;

        private bool ffIFF2 = false;

        private bool flagQ;

        private bool halted = false;

        private int memptr;

        private IntMode modeINT = IntMode.IM0;

        private bool pendingEI = false;

        private int regA, regB, regC, regD, regE, regH, regL;

        private int regAx;

        private int regBx, regCx, regDx, regEx, regHx, regLx;

        private int regF;

        private int regFx;

        private int regI;

        private int regIX;

        private int regIY;

        private int regPC;

        private int regR;

        private int regSP;

        public Z80State()
        {
        }

        public IntMode GetIM()
        {
            return modeINT;
        }

        public int GetMemPtr()
        {
            return memptr;
        }

        public int GetRegA()
        {
            return regA;
        }

        public int GetRegAF()
        {
            return (regA << 8) | regF;
        }

        public int GetRegAFx()
        {
            return (regAx << 8) | regFx;
        }

        public int GetRegAx()
        {
            return regAx;
        }

        public int GetRegB()
        {
            return regB;
        }

        public int GetRegBC()
        {
            return (regB << 8) | regC;
        }

        public int GetRegBCx()
        {
            return (regBx << 8) | regCx;
        }

        public int GetRegBx()
        {
            return regBx;
        }

        public int GetRegC()
        {
            return regC;
        }

        public int GetRegCx()
        {
            return regCx;
        }

        public int GetRegD()
        {
            return regD;
        }

        public int GetRegDE()
        {
            return (regD << 8) | regE;
        }

        public int GetRegDEx()
        {
            return (regDx << 8) | regEx;
        }

        public int GetRegDx()
        {
            return regDx;
        }

        public int GetRegE()
        {
            return regE;
        }

        public int GetRegEx()
        {
            return regEx;
        }

        public int GetRegF()
        {
            return regF;
        }

        public int GetRegFx()
        {
            return regFx;
        }

        public int GetRegH()
        {
            return regH;
        }

        public int GetRegHL()
        {
            return (regH << 8) | regL;
        }

        public int GetRegHLx()
        {
            return (regHx << 8) | regLx;
        }

        public int GetRegHx()
        {
            return regHx;
        }

        public int GetRegI()
        {
            return regI;
        }

        public int GetRegIX()
        {
            return regIX;
        }

        public int GetRegIY()
        {
            return regIY;
        }

        public int GetRegL()
        {
            return regL;
        }

        public int GetRegLx()
        {
            return regLx;
        }

        public int GetRegPC()
        {
            return regPC;
        }

        public int GetRegR()
        {
            return regR;
        }

        public int GetRegSP()
        {
            return regSP;
        }

        public virtual bool IsFlagQ()
        {
            return flagQ;
        }

        public bool IsHalted()
        {
            return halted;
        }

        public bool IsIFF1()
        {
            return ffIFF1;
        }

        public bool IsIFF2()
        {
            return ffIFF2;
        }

        public bool IsINTLine()
        {
            return activeINT;
        }

        public bool IsNMI()
        {
            return activeNMI;
        }

        public bool IsPendingEI()
        {
            return pendingEI;
        }

        public virtual void SetFlagQ(bool flagQ)
        {
            this.flagQ = flagQ;
        }

        public virtual void SetHalted(bool state)
        {
            halted = state;
        }

        public void SetIFF1(bool state)
        {
            ffIFF1 = state;
        }

        public void SetIFF2(bool state)
        {
            ffIFF2 = state;
        }

        public void SetIM(IntMode mode)
        {
            modeINT = mode;
        }

        public void SetINTLine(bool intLine)
        {
            activeINT = intLine;
        }

        public void SetMemPtr(int word)
        {
            memptr = word & 0xffff;
        }

        public void SetNMI(bool nmi)
        {
            activeNMI = nmi;
        }

        public void SetPendingEI(bool state)
        {
            pendingEI = state;
        }

        public void SetRegA(int value)
        {
            regA = value & 0xff;
        }

        public void SetRegAF(int word)
        {
            regA = (word >> 8) & 0xff;
            regF = word & 0xff;
        }

        public void SetRegAFx(int word)
        {
            regAx = (word >> 8) & 0xff;
            regFx = word & 0xff;
        }

        public void SetRegAx(int value)
        {
            regAx = value & 0xff;
        }

        public void SetRegB(int value)
        {
            regB = value & 0xff;
        }

        public void SetRegBC(int word)
        {
            regB = (word >> 8) & 0xff;
            regC = word & 0xff;
        }

        public void SetRegBCx(int word)
        {
            regBx = (word >> 8) & 0xff;
            regCx = word & 0xff;
        }

        public void SetRegBx(int value)
        {
            regBx = value & 0xff;
        }

        public void SetRegC(int value)
        {
            regC = value & 0xff;
        }

        public void SetRegCx(int value)
        {
            regCx = value & 0xff;
        }

        public void SetRegD(int value)
        {
            regD = value & 0xff;
        }

        public void SetRegDE(int word)
        {
            regD = (word >> 8) & 0xff;
            regE = word & 0xff;
        }

        public void SetRegDEx(int word)
        {
            regDx = (word >> 8) & 0xff;
            regEx = word & 0xff;
        }

        public void SetRegDx(int value)
        {
            regDx = value & 0xff;
        }

        public void SetRegE(int value)
        {
            regE = value & 0xff;
        }

        public void SetRegEx(int value)
        {
            regEx = value & 0xff;
        }

        public void SetRegF(int value)
        {
            regF = value & 0xff;
        }

        public void SetRegFx(int value)
        {
            regFx = value & 0xff;
        }

        public void SetRegH(int value)
        {
            regH = value & 0xff;
        }

        public void SetRegHL(int word)
        {
            regH = (word >> 8) & 0xff;
            regL = word & 0xff;
        }

        public void SetRegHLx(int word)
        {
            regHx = (word >> 8) & 0xff;
            regLx = word & 0xff;
        }

        public void SetRegHx(int value)
        {
            regHx = value & 0xff;
        }

        public void SetRegI(int value)
        {
            regI = value & 0xff;
        }

        public void SetRegIX(int word)
        {
            regIX = word & 0xffff;
        }

        public void SetRegIY(int word)
        {
            regIY = word & 0xffff;
        }

        public void SetRegL(int value)
        {
            regL = value & 0xff;
        }

        public void SetRegLx(int value)
        {
            regLx = value & 0xff;
        }

        public void SetRegPC(int address)
        {
            regPC = address & 0xffff;
        }

        public void SetRegR(int value)
        {
            regR = value & 0xff;
        }

        public void SetRegSP(int word)
        {
            regSP = word & 0xffff;
        }

        public void TriggerNMI()
        {
            activeNMI = true;
        }
    }
}