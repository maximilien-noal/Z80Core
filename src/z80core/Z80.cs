using System;

namespace Z80core
{
    public class Z80
    {
        private static readonly int ADDSUB_MASK = 0x02;

        private static readonly int BIT3_MASK = 0x08;

        private static readonly int BIT5_MASK = 0x20;

        private static readonly int CARRY_MASK = 0x01;

        private static readonly int FLAG_53_MASK = BIT5_MASK | BIT3_MASK;

        private static readonly int FLAG_SZ_MASK = SIGN_MASK | ZERO_MASK;

        private static readonly int FLAG_SZHN_MASK = FLAG_SZ_MASK | HALFCARRY_MASK | ADDSUB_MASK;

        private static readonly int FLAG_SZHP_MASK = FLAG_SZP_MASK | HALFCARRY_MASK;

        private static readonly int FLAG_SZP_MASK = FLAG_SZ_MASK | PARITY_MASK;

        private static readonly int HALFCARRY_MASK = 0x10;

        private static readonly int OVERFLOW_MASK = 0x04;

        private static readonly int PARITY_MASK = 0x04;

        private static readonly int SIGN_MASK = 0x80;

        private static readonly int[] sz53n_addTable = new int[256];

        private static readonly int[] sz53n_subTable = new int[256];

        private static readonly int[] sz53pn_addTable = new int[256];

        private static readonly int[] sz53pn_subTable = new int[256];

        private static readonly int ZERO_MASK = 0x40;

        private readonly bool[] breakpointAt = new bool[65536];

        private bool activeINT = false;

        private bool activeNMI = false;

        private bool carryFlag;

        private bool execDone = false;

        private bool ffIFF1 = false;

        private bool ffIFF2 = false;

        private bool flagQ, lastFlagQ;

        private bool halted = false;

        private MemIoOps MemIoImpl;

        private int memptr;

        private IntMode modeINT = IntMode.IM0;

        private INotifyOps NotifyImpl;

        private bool pendingEI = false;

        private bool pinReset = false;

        private int prefixOpcode = 0x00;

        private int regA, regB, regC, regD, regE, regH, regL;

        private int regAx;

        private int regBx, regCx, regDx, regEx, regHx, regLx;

        private int regFx;

        private int regI;

        private int regIX;

        private int regIY;

        private int regPC;

        private int regR;

        private bool regRbit7;

        private int regSP;

        private int sz5h3pnFlags;

        static Z80()
        {
            bool evenBits;
            for (int idx = 0; idx < 256; idx++)
            {
                if (idx > 0x7f)
                {
                    sz53n_addTable[idx] |= SIGN_MASK;
                }

                evenBits = true;
                for (int mask = 0x01; mask < 0x100; mask <<= 1)
                {
                    if ((idx & mask) != 0)
                    {
                        evenBits = !evenBits;
                    }
                }

                sz53n_addTable[idx] |= (idx & FLAG_53_MASK);
                sz53n_subTable[idx] = sz53n_addTable[idx] | ADDSUB_MASK;
                if (evenBits)
                {
                    sz53pn_addTable[idx] = sz53n_addTable[idx] | PARITY_MASK;
                    sz53pn_subTable[idx] = sz53n_subTable[idx] | PARITY_MASK;
                }
                else
                {
                    sz53pn_addTable[idx] = sz53n_addTable[idx];
                    sz53pn_subTable[idx] = sz53n_subTable[idx];
                }
            }

            sz53n_addTable[0] |= ZERO_MASK;
            sz53pn_addTable[0] |= ZERO_MASK;
            sz53n_subTable[0] |= ZERO_MASK;
            sz53pn_subTable[0] |= ZERO_MASK;
        }

        public Z80(MemIoOps memory, INotifyOps notify)
        {
            MemIoImpl = memory;
            NotifyImpl = notify;
            execDone = false;
            breakpointAt.Initialize();
            Reset();
        }

        public void Execute()
        {
            int opCode = MemIoImpl.FetchOpcode(regPC);
            regR++;
            if (prefixOpcode == 0 && breakpointAt[regPC])
            {
                opCode = NotifyImpl.Breakpoint(regPC, opCode);
            }

            regPC = (regPC + 1) & 0xffff;
            switch (prefixOpcode)
            {
                case 0x00:
                    flagQ = pendingEI = false;
                    DecodeOpcode(opCode);
                    break;

                case 0xDD:
                    prefixOpcode = 0;
                    regIX = DecodeDDFD(opCode, regIX);
                    break;

                case 0xED:
                    prefixOpcode = 0;
                    DecodeED(opCode);
                    break;

                case 0xFD:
                    prefixOpcode = 0;
                    regIY = DecodeDDFD(opCode, regIY);
                    break;

                default:
                    Console.WriteLine(String.Format("ERROR!: prefixOpcode = %02x, opCode = %02x", prefixOpcode, opCode));
                    break;
            }

            if (prefixOpcode != 0x00)
            {
                return;
            }

            lastFlagQ = flagQ;
            if (execDone)
            {
                NotifyImpl.ExecDone();
            }

            if (activeNMI)
            {
                activeNMI = false;
                Nmi();
                return;
            }

            if (ffIFF1 && !pendingEI && MemIoImpl.IsActiveINT())
            {
                Interruption();
            }
        }

        public int GetFlags()
        {
            return carryFlag ? sz5h3pnFlags | CARRY_MASK : sz5h3pnFlags;
        }

        public IntMode GetIM()
        {
            return modeINT;
        }

        public int GetMemPtr()
        {
            return memptr & 0xffff;
        }

        public int GetPairIR()
        {
            if (regRbit7)
            {
                return (regI << 8) | ((regR & 0x7f) | SIGN_MASK);
            }

            return (regI << 8) | (regR & 0x7f);
        }

        public int GetRegA()
        {
            return regA;
        }

        public int GetRegAF()
        {
            return (regA << 8) | (carryFlag ? sz5h3pnFlags | CARRY_MASK : sz5h3pnFlags);
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
            return regRbit7 ? (regR & 0x7f) | SIGN_MASK : regR & 0x7f;
        }

        public int GetRegSP()
        {
            return regSP;
        }

        public Z80State GetZ80State()
        {
            Z80State state = new Z80State();
            state.SetRegA(regA);
            state.SetRegF(GetFlags());
            state.SetRegB(regB);
            state.SetRegC(regC);
            state.SetRegD(regD);
            state.SetRegE(regE);
            state.SetRegH(regH);
            state.SetRegL(regL);
            state.SetRegAx(regAx);
            state.SetRegFx(regFx);
            state.SetRegBx(regBx);
            state.SetRegCx(regCx);
            state.SetRegDx(regDx);
            state.SetRegEx(regEx);
            state.SetRegHx(regHx);
            state.SetRegLx(regLx);
            state.SetRegIX(regIX);
            state.SetRegIY(regIY);
            state.SetRegSP(regSP);
            state.SetRegPC(regPC);
            state.SetRegI(regI);
            state.SetRegR(GetRegR());
            state.SetMemPtr(memptr);
            state.SetHalted(halted);
            state.SetIFF1(ffIFF1);
            state.SetIFF2(ffIFF2);
            state.SetIM(modeINT);
            state.SetINTLine(activeINT);
            state.SetPendingEI(pendingEI);
            state.SetNMI(activeNMI);
            state.SetFlagQ(lastFlagQ);
            return state;
        }

        public bool IsAddSubFlag()
        {
            return (sz5h3pnFlags & ADDSUB_MASK) != 0;
        }

        public bool IsBit3Flag()
        {
            return (sz5h3pnFlags & BIT3_MASK) != 0;
        }

        public bool IsBit5Flag()
        {
            return (sz5h3pnFlags & BIT5_MASK) != 0;
        }

        public bool IsBreakpoint(int address)
        {
            return breakpointAt[address & 0xffff];
        }

        public bool IsCarryFlag()
        {
            return carryFlag;
        }

        public virtual bool IsExecDone()
        {
            return execDone;
        }

        public bool IsHalfCarryFlag()
        {
            return (sz5h3pnFlags & HALFCARRY_MASK) != 0;
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

        public bool IsParOverFlag()
        {
            return (sz5h3pnFlags & PARITY_MASK) != 0;
        }

        public bool IsPendingEI()
        {
            return pendingEI;
        }

        public bool IsSignFlag()
        {
            return sz5h3pnFlags >= SIGN_MASK;
        }

        public bool IsZeroFlag()
        {
            return (sz5h3pnFlags & ZERO_MASK) != 0;
        }

        public void Reset()
        {
            if (pinReset)
            {
                pinReset = false;
            }
            else
            {
                regA = regAx = 0xff;
                SetFlags(0xff);
                regFx = 0xff;
                regB = regBx = 0xff;
                regC = regCx = 0xff;
                regD = regDx = 0xff;
                regE = regEx = 0xff;
                regH = regHx = 0xff;
                regL = regLx = 0xff;
                regIX = regIY = 0xffff;
                regSP = 0xffff;
                memptr = 0xffff;
            }

            regPC = 0;
            regI = regR = 0;
            regRbit7 = false;
            ffIFF1 = false;
            ffIFF2 = false;
            pendingEI = false;
            activeNMI = false;
            activeINT = false;
            halted = false;
            SetIM(IntMode.IM0);
            lastFlagQ = false;
            prefixOpcode = 0x00;
        }

        public virtual void ResetBreakpoints()
        {
            breakpointAt.Initialize();
        }

        public void SetAddSubFlag(bool state)
        {
            if (state)
            {
                sz5h3pnFlags |= ADDSUB_MASK;
            }
            else
            {
                sz5h3pnFlags &= ~ADDSUB_MASK;
            }
        }

        public void SetBit3Fag(bool state)
        {
            if (state)
            {
                sz5h3pnFlags |= BIT3_MASK;
            }
            else
            {
                sz5h3pnFlags &= ~BIT3_MASK;
            }
        }

        public void SetBit5Flag(bool state)
        {
            if (state)
            {
                sz5h3pnFlags |= BIT5_MASK;
            }
            else
            {
                sz5h3pnFlags &= ~BIT5_MASK;
            }
        }

        public void SetBreakpoint(int address, bool state)
        {
            breakpointAt[address & 0xffff] = state;
        }

        public void SetCarryFlag(bool state)
        {
            carryFlag = state;
        }

        public virtual void SetExecDone(bool state)
        {
            execDone = state;
        }

        public void SetFlags(int regF)
        {
            sz5h3pnFlags = regF & 0xfe;
            carryFlag = (regF & CARRY_MASK) != 0;
        }

        public void SetHalfCarryFlag(bool state)
        {
            if (state)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }
            else
            {
                sz5h3pnFlags &= ~HALFCARRY_MASK;
            }
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

        public virtual void SetMemIoHandler(MemIoOps memIo)
        {
            MemIoImpl = memIo;
        }

        public void SetMemPtr(int word)
        {
            memptr = word & 0xffff;
        }

        public void SetNMI(bool nmi)
        {
            activeNMI = nmi;
        }

        public virtual void SetNotifyHandler(INotifyOps notify)
        {
            NotifyImpl = notify;
        }

        public void SetParOverFlag(bool state)
        {
            if (state)
            {
                sz5h3pnFlags |= PARITY_MASK;
            }
            else
            {
                sz5h3pnFlags &= ~PARITY_MASK;
            }
        }

        public void SetPendingEI(bool state)
        {
            pendingEI = state;
        }

        public virtual void SetPinReset()
        {
            pinReset = true;
        }

        public void SetRegA(int value)
        {
            regA = value & 0xff;
        }

        public void SetRegAF(int word)
        {
            regA = (word >> 8) & 0xff;
            sz5h3pnFlags = word & 0xfe;
            carryFlag = (word & CARRY_MASK) != 0;
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
            regR = value & 0x7f;
            regRbit7 = (value > 0x7f);
        }

        public void SetRegSP(int word)
        {
            regSP = word & 0xffff;
        }

        public void SetSignFlag(bool state)
        {
            if (state)
            {
                sz5h3pnFlags |= SIGN_MASK;
            }
            else
            {
                sz5h3pnFlags &= ~SIGN_MASK;
            }
        }

        public void SetZ80State(Z80State state)
        {
            regA = state.GetRegA();
            SetFlags(state.GetRegF());
            regB = state.GetRegB();
            regC = state.GetRegC();
            regD = state.GetRegD();
            regE = state.GetRegE();
            regH = state.GetRegH();
            regL = state.GetRegL();
            regAx = state.GetRegAx();
            regFx = state.GetRegFx();
            regBx = state.GetRegBx();
            regCx = state.GetRegCx();
            regDx = state.GetRegDx();
            regEx = state.GetRegEx();
            regHx = state.GetRegHx();
            regLx = state.GetRegLx();
            regIX = state.GetRegIX();
            regIY = state.GetRegIY();
            regSP = state.GetRegSP();
            regPC = state.GetRegPC();
            regI = state.GetRegI();
            SetRegR(state.GetRegR());
            memptr = state.GetMemPtr();
            halted = state.IsHalted();
            ffIFF1 = state.IsIFF1();
            ffIFF2 = state.IsIFF2();
            modeINT = state.GetIM();
            activeINT = state.IsINTLine();
            pendingEI = state.IsPendingEI();
            activeNMI = state.IsNMI();
            flagQ = false;
            lastFlagQ = state.IsFlagQ();
        }

        public void SetZeroFlag(bool state)
        {
            if (state)
            {
                sz5h3pnFlags |= ZERO_MASK;
            }
            else
            {
                sz5h3pnFlags &= ~ZERO_MASK;
            }
        }

        public void TriggerNMI()
        {
            activeNMI = true;
        }

        private void Adc(int oper8)
        {
            int res = regA + oper8;
            if (carryFlag)
            {
                res++;
            }

            carryFlag = res > 0xff;
            res &= 0xff;
            sz5h3pnFlags = sz53n_addTable[res];
            if (((regA ^ oper8 ^ res) & 0x10) != 0)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            if (((regA ^ ~oper8) & (regA ^ res)) > 0x7f)
            {
                sz5h3pnFlags |= OVERFLOW_MASK;
            }

            regA = res;
            flagQ = true;
        }

        private void Adc16(int reg16)
        {
            int regHL = GetRegHL();
            memptr = regHL + 1;
            int res = regHL + reg16;
            if (carryFlag)
            {
                res++;
            }

            carryFlag = res > 0xffff;
            res &= 0xffff;
            SetRegHL(res);
            sz5h3pnFlags = sz53n_addTable[regH];
            if (res != 0)
            {
                sz5h3pnFlags &= ~ZERO_MASK;
            }

            if (((res ^ regHL ^ reg16) & 0x1000) != 0)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            if (((regHL ^ ~reg16) & (regHL ^ res)) > 0x7fff)
            {
                sz5h3pnFlags |= OVERFLOW_MASK;
            }

            flagQ = true;
        }

        private void Add(int oper8)
        {
            int res = regA + oper8;
            carryFlag = res > 0xff;
            res &= 0xff;
            sz5h3pnFlags = sz53n_addTable[res];
            if ((res & 0x0f) < (regA & 0x0f))
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            if (((regA ^ ~oper8) & (regA ^ res)) > 0x7f)
            {
                sz5h3pnFlags |= OVERFLOW_MASK;
            }

            regA = res;
            flagQ = true;
        }

        private int Add16(int reg16, int oper16)
        {
            oper16 += reg16;
            carryFlag = oper16 > 0xffff;
            sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZP_MASK) | ((oper16 >> 8) & FLAG_53_MASK);
            oper16 &= 0xffff;
            if ((oper16 & 0x0fff) < (reg16 & 0x0fff))
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            memptr = reg16 + 1;
            flagQ = true;
            return oper16;
        }

        private void And(int oper8)
        {
            regA &= oper8;
            carryFlag = false;
            sz5h3pnFlags = sz53pn_addTable[regA] | HALFCARRY_MASK;
            flagQ = true;
        }

        private void Bit(int mask, int reg)
        {
            bool zeroFlag = (mask & reg) == 0;
            sz5h3pnFlags = (sz53n_addTable[reg] & ~FLAG_SZP_MASK) | HALFCARRY_MASK;
            if (zeroFlag)
            {
                sz5h3pnFlags |= (PARITY_MASK | ZERO_MASK);
            }

            if (mask == SIGN_MASK && !zeroFlag)
            {
                sz5h3pnFlags |= SIGN_MASK;
            }

            flagQ = true;
        }

        private void CopyToRegister(int opCode, int value)
        {
            switch (opCode & 0x07)
            {
                case 0x00:
                    regB = value;
                    break;

                case 0x01:
                    regC = value;
                    break;

                case 0x02:
                    regD = value;
                    break;

                case 0x03:
                    regE = value;
                    break;

                case 0x04:
                    regH = value;
                    break;

                case 0x05:
                    regL = value;
                    break;

                case 0x07:
                    regA = value;
                    break;

                default:
                    break;
            }
        }

        private void Cp(int oper8)
        {
            int res = regA - (oper8 & 0xff);
            carryFlag = res < 0;
            res &= 0xff;
            sz5h3pnFlags = (sz53n_addTable[oper8] & FLAG_53_MASK) | (sz53n_subTable[res] & FLAG_SZHN_MASK);
            if ((res & 0x0f) > (regA & 0x0f))
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            if (((regA ^ oper8) & (regA ^ res)) > 0x7f)
            {
                sz5h3pnFlags |= OVERFLOW_MASK;
            }

            flagQ = true;
        }

        private void Cpd()
        {
            int regHL = GetRegHL();
            int memHL = MemIoImpl.Peek8(regHL);
            bool carry = carryFlag;
            Cp(memHL);
            carryFlag = carry;
            MemIoImpl.AddressOnBus(regHL, 5);
            DecRegHL();
            DecRegBC();
            memHL = regA - memHL - ((sz5h3pnFlags & HALFCARRY_MASK) != 0 ? 1 : 0);
            sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHN_MASK) | (memHL & BIT3_MASK);
            if ((memHL & ADDSUB_MASK) != 0)
            {
                sz5h3pnFlags |= BIT5_MASK;
            }

            if (regC != 0 || regB != 0)
            {
                sz5h3pnFlags |= PARITY_MASK;
            }

            memptr--;
            flagQ = true;
        }

        private void Cpi()
        {
            int regHL = GetRegHL();
            int memHL = MemIoImpl.Peek8(regHL);
            bool carry = carryFlag;
            Cp(memHL);
            carryFlag = carry;
            MemIoImpl.AddressOnBus(regHL, 5);
            IncRegHL();
            DecRegBC();
            memHL = regA - memHL - ((sz5h3pnFlags & HALFCARRY_MASK) != 0 ? 1 : 0);
            sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHN_MASK) | (memHL & BIT3_MASK);
            if ((memHL & ADDSUB_MASK) != 0)
            {
                sz5h3pnFlags |= BIT5_MASK;
            }

            if (regC != 0 || regB != 0)
            {
                sz5h3pnFlags |= PARITY_MASK;
            }

            memptr++;
            flagQ = true;
        }

        private void Daa()
        {
            int suma = 0;
            bool carry = carryFlag;
            if ((sz5h3pnFlags & HALFCARRY_MASK) != 0 || (regA & 0x0f) > 0x09)
            {
                suma = 6;
            }

            if (carry || (regA > 0x99))
            {
                suma |= 0x60;
            }

            if (regA > 0x99)
            {
                carry = true;
            }

            if ((sz5h3pnFlags & ADDSUB_MASK) != 0)
            {
                Sub(suma);
                sz5h3pnFlags = (sz5h3pnFlags & HALFCARRY_MASK) | sz53pn_subTable[regA];
            }
            else
            {
                Add(suma);
                sz5h3pnFlags = (sz5h3pnFlags & HALFCARRY_MASK) | sz53pn_addTable[regA];
            }

            carryFlag = carry;
            flagQ = true;
        }

        private int Dec8(int oper8)
        {
            oper8 = (oper8 - 1) & 0xff;
            sz5h3pnFlags = sz53n_subTable[oper8];
            if ((oper8 & 0x0f) == 0x0f)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            if (oper8 == 0x7f)
            {
                sz5h3pnFlags |= OVERFLOW_MASK;
            }

            flagQ = true;
            return oper8;
        }

        private void DecodeCB()
        {
            int opCode = MemIoImpl.FetchOpcode(regPC);
            regPC = (regPC + 1) & 0xffff;
            regR++;
            switch (opCode)
            {
                case 0x00:
                    {
                        regB = Rlc(regB);
                        break;
                    }

                case 0x01:
                    {
                        regC = Rlc(regC);
                        break;
                    }

                case 0x02:
                    {
                        regD = Rlc(regD);
                        break;
                    }

                case 0x03:
                    {
                        regE = Rlc(regE);
                        break;
                    }

                case 0x04:
                    {
                        regH = Rlc(regH);
                        break;
                    }

                case 0x05:
                    {
                        regL = Rlc(regL);
                        break;
                    }

                case 0x06:
                    {
                        int work16 = GetRegHL();
                        int work8 = Rlc(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x07:
                    {
                        regA = Rlc(regA);
                        break;
                    }

                case 0x08:
                    {
                        regB = Rrc(regB);
                        break;
                    }

                case 0x09:
                    {
                        regC = Rrc(regC);
                        break;
                    }

                case 0x0A:
                    {
                        regD = Rrc(regD);
                        break;
                    }

                case 0x0B:
                    {
                        regE = Rrc(regE);
                        break;
                    }

                case 0x0C:
                    {
                        regH = Rrc(regH);
                        break;
                    }

                case 0x0D:
                    {
                        regL = Rrc(regL);
                        break;
                    }

                case 0x0E:
                    {
                        int work16 = GetRegHL();
                        int work8 = Rrc(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x0F:
                    {
                        regA = Rrc(regA);
                        break;
                    }

                case 0x10:
                    {
                        regB = Rl(regB);
                        break;
                    }

                case 0x11:
                    {
                        regC = Rl(regC);
                        break;
                    }

                case 0x12:
                    {
                        regD = Rl(regD);
                        break;
                    }

                case 0x13:
                    {
                        regE = Rl(regE);
                        break;
                    }

                case 0x14:
                    {
                        regH = Rl(regH);
                        break;
                    }

                case 0x15:
                    {
                        regL = Rl(regL);
                        break;
                    }

                case 0x16:
                    {
                        int work16 = GetRegHL();
                        int work8 = Rl(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x17:
                    {
                        regA = Rl(regA);
                        break;
                    }

                case 0x18:
                    {
                        regB = Rr(regB);
                        break;
                    }

                case 0x19:
                    {
                        regC = Rr(regC);
                        break;
                    }

                case 0x1A:
                    {
                        regD = Rr(regD);
                        break;
                    }

                case 0x1B:
                    {
                        regE = Rr(regE);
                        break;
                    }

                case 0x1C:
                    {
                        regH = Rr(regH);
                        break;
                    }

                case 0x1D:
                    {
                        regL = Rr(regL);
                        break;
                    }

                case 0x1E:
                    {
                        int work16 = GetRegHL();
                        int work8 = Rr(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x1F:
                    {
                        regA = Rr(regA);
                        break;
                    }

                case 0x20:
                    {
                        regB = Sla(regB);
                        break;
                    }

                case 0x21:
                    {
                        regC = Sla(regC);
                        break;
                    }

                case 0x22:
                    {
                        regD = Sla(regD);
                        break;
                    }

                case 0x23:
                    {
                        regE = Sla(regE);
                        break;
                    }

                case 0x24:
                    {
                        regH = Sla(regH);
                        break;
                    }

                case 0x25:
                    {
                        regL = Sla(regL);
                        break;
                    }

                case 0x26:
                    {
                        int work16 = GetRegHL();
                        int work8 = Sla(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x27:
                    {
                        regA = Sla(regA);
                        break;
                    }

                case 0x28:
                    {
                        regB = Sra(regB);
                        break;
                    }

                case 0x29:
                    {
                        regC = Sra(regC);
                        break;
                    }

                case 0x2A:
                    {
                        regD = Sra(regD);
                        break;
                    }

                case 0x2B:
                    {
                        regE = Sra(regE);
                        break;
                    }

                case 0x2C:
                    {
                        regH = Sra(regH);
                        break;
                    }

                case 0x2D:
                    {
                        regL = Sra(regL);
                        break;
                    }

                case 0x2E:
                    {
                        int work16 = GetRegHL();
                        int work8 = Sra(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x2F:
                    {
                        regA = Sra(regA);
                        break;
                    }

                case 0x30:
                    {
                        regB = Sll(regB);
                        break;
                    }

                case 0x31:
                    {
                        regC = Sll(regC);
                        break;
                    }

                case 0x32:
                    {
                        regD = Sll(regD);
                        break;
                    }

                case 0x33:
                    {
                        regE = Sll(regE);
                        break;
                    }

                case 0x34:
                    {
                        regH = Sll(regH);
                        break;
                    }

                case 0x35:
                    {
                        regL = Sll(regL);
                        break;
                    }

                case 0x36:
                    {
                        int work16 = GetRegHL();
                        int work8 = Sll(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x37:
                    {
                        regA = Sll(regA);
                        break;
                    }

                case 0x38:
                    {
                        regB = Srl(regB);
                        break;
                    }

                case 0x39:
                    {
                        regC = Srl(regC);
                        break;
                    }

                case 0x3A:
                    {
                        regD = Srl(regD);
                        break;
                    }

                case 0x3B:
                    {
                        regE = Srl(regE);
                        break;
                    }

                case 0x3C:
                    {
                        regH = Srl(regH);
                        break;
                    }

                case 0x3D:
                    {
                        regL = Srl(regL);
                        break;
                    }

                case 0x3E:
                    {
                        int work16 = GetRegHL();
                        int work8 = Srl(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x3F:
                    {
                        regA = Srl(regA);
                        break;
                    }

                case 0x40:
                    {
                        Bit(0x01, regB);
                        break;
                    }

                case 0x41:
                    {
                        Bit(0x01, regC);
                        break;
                    }

                case 0x42:
                    {
                        Bit(0x01, regD);
                        break;
                    }

                case 0x43:
                    {
                        Bit(0x01, regE);
                        break;
                    }

                case 0x44:
                    {
                        Bit(0x01, regH);
                        break;
                    }

                case 0x45:
                    {
                        Bit(0x01, regL);
                        break;
                    }

                case 0x46:
                    {
                        int work16 = GetRegHL();
                        Bit(0x01, MemIoImpl.Peek8(work16));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((memptr >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(work16, 1);
                        break;
                    }

                case 0x47:
                    {
                        Bit(0x01, regA);
                        break;
                    }

                case 0x48:
                    {
                        Bit(0x02, regB);
                        break;
                    }

                case 0x49:
                    {
                        Bit(0x02, regC);
                        break;
                    }

                case 0x4A:
                    {
                        Bit(0x02, regD);
                        break;
                    }

                case 0x4B:
                    {
                        Bit(0x02, regE);
                        break;
                    }

                case 0x4C:
                    {
                        Bit(0x02, regH);
                        break;
                    }

                case 0x4D:
                    {
                        Bit(0x02, regL);
                        break;
                    }

                case 0x4E:
                    {
                        int work16 = GetRegHL();
                        Bit(0x02, MemIoImpl.Peek8(work16));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((memptr >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(work16, 1);
                        break;
                    }

                case 0x4F:
                    {
                        Bit(0x02, regA);
                        break;
                    }

                case 0x50:
                    {
                        Bit(0x04, regB);
                        break;
                    }

                case 0x51:
                    {
                        Bit(0x04, regC);
                        break;
                    }

                case 0x52:
                    {
                        Bit(0x04, regD);
                        break;
                    }

                case 0x53:
                    {
                        Bit(0x04, regE);
                        break;
                    }

                case 0x54:
                    {
                        Bit(0x04, regH);
                        break;
                    }

                case 0x55:
                    {
                        Bit(0x04, regL);
                        break;
                    }

                case 0x56:
                    {
                        int work16 = GetRegHL();
                        Bit(0x04, MemIoImpl.Peek8(work16));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((memptr >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(work16, 1);
                        break;
                    }

                case 0x57:
                    {
                        Bit(0x04, regA);
                        break;
                    }

                case 0x58:
                    {
                        Bit(0x08, regB);
                        break;
                    }

                case 0x59:
                    {
                        Bit(0x08, regC);
                        break;
                    }

                case 0x5A:
                    {
                        Bit(0x08, regD);
                        break;
                    }

                case 0x5B:
                    {
                        Bit(0x08, regE);
                        break;
                    }

                case 0x5C:
                    {
                        Bit(0x08, regH);
                        break;
                    }

                case 0x5D:
                    {
                        Bit(0x08, regL);
                        break;
                    }

                case 0x5E:
                    {
                        int work16 = GetRegHL();
                        Bit(0x08, MemIoImpl.Peek8(work16));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((memptr >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(work16, 1);
                        break;
                    }

                case 0x5F:
                    {
                        Bit(0x08, regA);
                        break;
                    }

                case 0x60:
                    {
                        Bit(0x10, regB);
                        break;
                    }

                case 0x61:
                    {
                        Bit(0x10, regC);
                        break;
                    }

                case 0x62:
                    {
                        Bit(0x10, regD);
                        break;
                    }

                case 0x63:
                    {
                        Bit(0x10, regE);
                        break;
                    }

                case 0x64:
                    {
                        Bit(0x10, regH);
                        break;
                    }

                case 0x65:
                    {
                        Bit(0x10, regL);
                        break;
                    }

                case 0x66:
                    {
                        int work16 = GetRegHL();
                        Bit(0x10, MemIoImpl.Peek8(work16));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((memptr >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(work16, 1);
                        break;
                    }

                case 0x67:
                    {
                        Bit(0x10, regA);
                        break;
                    }

                case 0x68:
                    {
                        Bit(0x20, regB);
                        break;
                    }

                case 0x69:
                    {
                        Bit(0x20, regC);
                        break;
                    }

                case 0x6A:
                    {
                        Bit(0x20, regD);
                        break;
                    }

                case 0x6B:
                    {
                        Bit(0x20, regE);
                        break;
                    }

                case 0x6C:
                    {
                        Bit(0x20, regH);
                        break;
                    }

                case 0x6D:
                    {
                        Bit(0x20, regL);
                        break;
                    }

                case 0x6E:
                    {
                        int work16 = GetRegHL();
                        Bit(0x20, MemIoImpl.Peek8(work16));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((memptr >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(work16, 1);
                        break;
                    }

                case 0x6F:
                    {
                        Bit(0x20, regA);
                        break;
                    }

                case 0x70:
                    {
                        Bit(0x40, regB);
                        break;
                    }

                case 0x71:
                    {
                        Bit(0x40, regC);
                        break;
                    }

                case 0x72:
                    {
                        Bit(0x40, regD);
                        break;
                    }

                case 0x73:
                    {
                        Bit(0x40, regE);
                        break;
                    }

                case 0x74:
                    {
                        Bit(0x40, regH);
                        break;
                    }

                case 0x75:
                    {
                        Bit(0x40, regL);
                        break;
                    }

                case 0x76:
                    {
                        int work16 = GetRegHL();
                        Bit(0x40, MemIoImpl.Peek8(work16));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((memptr >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(work16, 1);
                        break;
                    }

                case 0x77:
                    {
                        Bit(0x40, regA);
                        break;
                    }

                case 0x78:
                    {
                        Bit(0x80, regB);
                        break;
                    }

                case 0x79:
                    {
                        Bit(0x80, regC);
                        break;
                    }

                case 0x7A:
                    {
                        Bit(0x80, regD);
                        break;
                    }

                case 0x7B:
                    {
                        Bit(0x80, regE);
                        break;
                    }

                case 0x7C:
                    {
                        Bit(0x80, regH);
                        break;
                    }

                case 0x7D:
                    {
                        Bit(0x80, regL);
                        break;
                    }

                case 0x7E:
                    {
                        int work16 = GetRegHL();
                        Bit(0x80, MemIoImpl.Peek8(work16));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((memptr >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(work16, 1);
                        break;
                    }

                case 0x7F:
                    {
                        Bit(0x80, regA);
                        break;
                    }

                case 0x80:
                    {
                        regB &= 0xFE;
                        break;
                    }

                case 0x81:
                    {
                        regC &= 0xFE;
                        break;
                    }

                case 0x82:
                    {
                        regD &= 0xFE;
                        break;
                    }

                case 0x83:
                    {
                        regE &= 0xFE;
                        break;
                    }

                case 0x84:
                    {
                        regH &= 0xFE;
                        break;
                    }

                case 0x85:
                    {
                        regL &= 0xFE;
                        break;
                    }

                case 0x86:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) & 0xFE;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x87:
                    {
                        regA &= 0xFE;
                        break;
                    }

                case 0x88:
                    {
                        regB &= 0xFD;
                        break;
                    }

                case 0x89:
                    {
                        regC &= 0xFD;
                        break;
                    }

                case 0x8A:
                    {
                        regD &= 0xFD;
                        break;
                    }

                case 0x8B:
                    {
                        regE &= 0xFD;
                        break;
                    }

                case 0x8C:
                    {
                        regH &= 0xFD;
                        break;
                    }

                case 0x8D:
                    {
                        regL &= 0xFD;
                        break;
                    }

                case 0x8E:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) & 0xFD;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x8F:
                    {
                        regA &= 0xFD;
                        break;
                    }

                case 0x90:
                    {
                        regB &= 0xFB;
                        break;
                    }

                case 0x91:
                    {
                        regC &= 0xFB;
                        break;
                    }

                case 0x92:
                    {
                        regD &= 0xFB;
                        break;
                    }

                case 0x93:
                    {
                        regE &= 0xFB;
                        break;
                    }

                case 0x94:
                    {
                        regH &= 0xFB;
                        break;
                    }

                case 0x95:
                    {
                        regL &= 0xFB;
                        break;
                    }

                case 0x96:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) & 0xFB;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x97:
                    {
                        regA &= 0xFB;
                        break;
                    }

                case 0x98:
                    {
                        regB &= 0xF7;
                        break;
                    }

                case 0x99:
                    {
                        regC &= 0xF7;
                        break;
                    }

                case 0x9A:
                    {
                        regD &= 0xF7;
                        break;
                    }

                case 0x9B:
                    {
                        regE &= 0xF7;
                        break;
                    }

                case 0x9C:
                    {
                        regH &= 0xF7;
                        break;
                    }

                case 0x9D:
                    {
                        regL &= 0xF7;
                        break;
                    }

                case 0x9E:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) & 0xF7;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x9F:
                    {
                        regA &= 0xF7;
                        break;
                    }

                case 0xA0:
                    {
                        regB &= 0xEF;
                        break;
                    }

                case 0xA1:
                    {
                        regC &= 0xEF;
                        break;
                    }

                case 0xA2:
                    {
                        regD &= 0xEF;
                        break;
                    }

                case 0xA3:
                    {
                        regE &= 0xEF;
                        break;
                    }

                case 0xA4:
                    {
                        regH &= 0xEF;
                        break;
                    }

                case 0xA5:
                    {
                        regL &= 0xEF;
                        break;
                    }

                case 0xA6:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) & 0xEF;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xA7:
                    {
                        regA &= 0xEF;
                        break;
                    }

                case 0xA8:
                    {
                        regB &= 0xDF;
                        break;
                    }

                case 0xA9:
                    {
                        regC &= 0xDF;
                        break;
                    }

                case 0xAA:
                    {
                        regD &= 0xDF;
                        break;
                    }

                case 0xAB:
                    {
                        regE &= 0xDF;
                        break;
                    }

                case 0xAC:
                    {
                        regH &= 0xDF;
                        break;
                    }

                case 0xAD:
                    {
                        regL &= 0xDF;
                        break;
                    }

                case 0xAE:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) & 0xDF;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xAF:
                    {
                        regA &= 0xDF;
                        break;
                    }

                case 0xB0:
                    {
                        regB &= 0xBF;
                        break;
                    }

                case 0xB1:
                    {
                        regC &= 0xBF;
                        break;
                    }

                case 0xB2:
                    {
                        regD &= 0xBF;
                        break;
                    }

                case 0xB3:
                    {
                        regE &= 0xBF;
                        break;
                    }

                case 0xB4:
                    {
                        regH &= 0xBF;
                        break;
                    }

                case 0xB5:
                    {
                        regL &= 0xBF;
                        break;
                    }

                case 0xB6:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) & 0xBF;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xB7:
                    {
                        regA &= 0xBF;
                        break;
                    }

                case 0xB8:
                    {
                        regB &= 0x7F;
                        break;
                    }

                case 0xB9:
                    {
                        regC &= 0x7F;
                        break;
                    }

                case 0xBA:
                    {
                        regD &= 0x7F;
                        break;
                    }

                case 0xBB:
                    {
                        regE &= 0x7F;
                        break;
                    }

                case 0xBC:
                    {
                        regH &= 0x7F;
                        break;
                    }

                case 0xBD:
                    {
                        regL &= 0x7F;
                        break;
                    }

                case 0xBE:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) & 0x7F;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xBF:
                    {
                        regA &= 0x7F;
                        break;
                    }

                case 0xC0:
                    {
                        regB |= 0x01;
                        break;
                    }

                case 0xC1:
                    {
                        regC |= 0x01;
                        break;
                    }

                case 0xC2:
                    {
                        regD |= 0x01;
                        break;
                    }

                case 0xC3:
                    {
                        regE |= 0x01;
                        break;
                    }

                case 0xC4:
                    {
                        regH |= 0x01;
                        break;
                    }

                case 0xC5:
                    {
                        regL |= 0x01;
                        break;
                    }

                case 0xC6:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) | 0x01;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xC7:
                    {
                        regA |= 0x01;
                        break;
                    }

                case 0xC8:
                    {
                        regB |= 0x02;
                        break;
                    }

                case 0xC9:
                    {
                        regC |= 0x02;
                        break;
                    }

                case 0xCA:
                    {
                        regD |= 0x02;
                        break;
                    }

                case 0xCB:
                    {
                        regE |= 0x02;
                        break;
                    }

                case 0xCC:
                    {
                        regH |= 0x02;
                        break;
                    }

                case 0xCD:
                    {
                        regL |= 0x02;
                        break;
                    }

                case 0xCE:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) | 0x02;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xCF:
                    {
                        regA |= 0x02;
                        break;
                    }

                case 0xD0:
                    {
                        regB |= 0x04;
                        break;
                    }

                case 0xD1:
                    {
                        regC |= 0x04;
                        break;
                    }

                case 0xD2:
                    {
                        regD |= 0x04;
                        break;
                    }

                case 0xD3:
                    {
                        regE |= 0x04;
                        break;
                    }

                case 0xD4:
                    {
                        regH |= 0x04;
                        break;
                    }

                case 0xD5:
                    {
                        regL |= 0x04;
                        break;
                    }

                case 0xD6:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) | 0x04;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xD7:
                    {
                        regA |= 0x04;
                        break;
                    }

                case 0xD8:
                    {
                        regB |= 0x08;
                        break;
                    }

                case 0xD9:
                    {
                        regC |= 0x08;
                        break;
                    }

                case 0xDA:
                    {
                        regD |= 0x08;
                        break;
                    }

                case 0xDB:
                    {
                        regE |= 0x08;
                        break;
                    }

                case 0xDC:
                    {
                        regH |= 0x08;
                        break;
                    }

                case 0xDD:
                    {
                        regL |= 0x08;
                        break;
                    }

                case 0xDE:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) | 0x08;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xDF:
                    {
                        regA |= 0x08;
                        break;
                    }

                case 0xE0:
                    {
                        regB |= 0x10;
                        break;
                    }

                case 0xE1:
                    {
                        regC |= 0x10;
                        break;
                    }

                case 0xE2:
                    {
                        regD |= 0x10;
                        break;
                    }

                case 0xE3:
                    {
                        regE |= 0x10;
                        break;
                    }

                case 0xE4:
                    {
                        regH |= 0x10;
                        break;
                    }

                case 0xE5:
                    {
                        regL |= 0x10;
                        break;
                    }

                case 0xE6:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) | 0x10;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xE7:
                    {
                        regA |= 0x10;
                        break;
                    }

                case 0xE8:
                    {
                        regB |= 0x20;
                        break;
                    }

                case 0xE9:
                    {
                        regC |= 0x20;
                        break;
                    }

                case 0xEA:
                    {
                        regD |= 0x20;
                        break;
                    }

                case 0xEB:
                    {
                        regE |= 0x20;
                        break;
                    }

                case 0xEC:
                    {
                        regH |= 0x20;
                        break;
                    }

                case 0xED:
                    {
                        regL |= 0x20;
                        break;
                    }

                case 0xEE:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) | 0x20;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xEF:
                    {
                        regA |= 0x20;
                        break;
                    }

                case 0xF0:
                    {
                        regB |= 0x40;
                        break;
                    }

                case 0xF1:
                    {
                        regC |= 0x40;
                        break;
                    }

                case 0xF2:
                    {
                        regD |= 0x40;
                        break;
                    }

                case 0xF3:
                    {
                        regE |= 0x40;
                        break;
                    }

                case 0xF4:
                    {
                        regH |= 0x40;
                        break;
                    }

                case 0xF5:
                    {
                        regL |= 0x40;
                        break;
                    }

                case 0xF6:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) | 0x40;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xF7:
                    {
                        regA |= 0x40;
                        break;
                    }

                case 0xF8:
                    {
                        regB |= 0x80;
                        break;
                    }

                case 0xF9:
                    {
                        regC |= 0x80;
                        break;
                    }

                case 0xFA:
                    {
                        regD |= 0x80;
                        break;
                    }

                case 0xFB:
                    {
                        regE |= 0x80;
                        break;
                    }

                case 0xFC:
                    {
                        regH |= 0x80;
                        break;
                    }

                case 0xFD:
                    {
                        regL |= 0x80;
                        break;
                    }

                case 0xFE:
                    {
                        int work16 = GetRegHL();
                        int work8 = MemIoImpl.Peek8(work16) | 0x80;
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0xFF:
                    {
                        regA |= 0x80;
                        break;
                    }

                default:
                    {
                        break;
                    }
            }
        }

        private int DecodeDDFD(int opCode, int regIXY)
        {
            switch (opCode)
            {
                case 0x09:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        regIXY = Add16(regIXY, GetRegBC());
                        break;
                    }

                case 0x19:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        regIXY = Add16(regIXY, GetRegDE());
                        break;
                    }

                case 0x21:
                    {
                        regIXY = MemIoImpl.Peek16(regPC);
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x22:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        MemIoImpl.Poke16(memptr++, regIXY);
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x23:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        regIXY = (regIXY + 1) & 0xffff;
                        break;
                    }

                case 0x24:
                    {
                        regIXY = (Inc8(regIXY >> 8) << 8) | (regIXY & 0xff);
                        break;
                    }

                case 0x25:
                    {
                        regIXY = (Dec8(regIXY >> 8) << 8) | (regIXY & 0xff);
                        break;
                    }

                case 0x26:
                    {
                        regIXY = (MemIoImpl.Peek8(regPC) << 8) | (regIXY & 0xff);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x29:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        regIXY = Add16(regIXY, regIXY);
                        break;
                    }

                case 0x2A:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        regIXY = MemIoImpl.Peek16(memptr++);
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x2B:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        regIXY = (regIXY - 1) & 0xffff;
                        break;
                    }

                case 0x2C:
                    {
                        regIXY = (regIXY & 0xff00) | Inc8(regIXY & 0xff);
                        break;
                    }

                case 0x2D:
                    {
                        regIXY = (regIXY & 0xff00) | Dec8(regIXY & 0xff);
                        break;
                    }

                case 0x2E:
                    {
                        regIXY = (regIXY & 0xff00) | MemIoImpl.Peek8(regPC);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x34:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        int work8 = MemIoImpl.Peek8(memptr);
                        MemIoImpl.AddressOnBus(memptr, 1);
                        MemIoImpl.Poke8(memptr, Inc8(work8));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x35:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        int work8 = MemIoImpl.Peek8(memptr);
                        MemIoImpl.AddressOnBus(memptr, 1);
                        MemIoImpl.Poke8(memptr, Dec8(work8));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x36:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        regPC = (regPC + 1) & 0xffff;
                        int work8 = MemIoImpl.Peek8(regPC);
                        MemIoImpl.AddressOnBus(regPC, 2);
                        regPC = (regPC + 1) & 0xffff;
                        MemIoImpl.Poke8(memptr, work8);
                        break;
                    }

                case 0x39:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        regIXY = Add16(regIXY, regSP);
                        break;
                    }

                case 0x44:
                    {
                        regB = regIXY >> 8;
                        break;
                    }

                case 0x45:
                    {
                        regB = regIXY & 0xff;
                        break;
                    }

                case 0x46:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        regB = MemIoImpl.Peek8(memptr);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x4C:
                    {
                        regC = regIXY >> 8;
                        break;
                    }

                case 0x4D:
                    {
                        regC = regIXY & 0xff;
                        break;
                    }

                case 0x4E:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        regC = MemIoImpl.Peek8(memptr);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x54:
                    {
                        regD = regIXY >> 8;
                        break;
                    }

                case 0x55:
                    {
                        regD = regIXY & 0xff;
                        break;
                    }

                case 0x56:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        regD = MemIoImpl.Peek8(memptr);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x5C:
                    {
                        regE = regIXY >> 8;
                        break;
                    }

                case 0x5D:
                    {
                        regE = regIXY & 0xff;
                        break;
                    }

                case 0x5E:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        regE = MemIoImpl.Peek8(memptr);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x60:
                    {
                        regIXY = (regIXY & 0x00ff) | (regB << 8);
                        break;
                    }

                case 0x61:
                    {
                        regIXY = (regIXY & 0x00ff) | (regC << 8);
                        break;
                    }

                case 0x62:
                    {
                        regIXY = (regIXY & 0x00ff) | (regD << 8);
                        break;
                    }

                case 0x63:
                    {
                        regIXY = (regIXY & 0x00ff) | (regE << 8);
                        break;
                    }

                case 0x64:
                    {
                        break;
                    }

                case 0x65:
                    {
                        regIXY = (regIXY & 0x00ff) | ((regIXY & 0xff) << 8);
                        break;
                    }

                case 0x66:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        regH = MemIoImpl.Peek8(memptr);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x67:
                    {
                        regIXY = (regIXY & 0x00ff) | (regA << 8);
                        break;
                    }

                case 0x68:
                    {
                        regIXY = (regIXY & 0xff00) | regB;
                        break;
                    }

                case 0x69:
                    {
                        regIXY = (regIXY & 0xff00) | regC;
                        break;
                    }

                case 0x6A:
                    {
                        regIXY = (regIXY & 0xff00) | regD;
                        break;
                    }

                case 0x6B:
                    {
                        regIXY = (regIXY & 0xff00) | regE;
                        break;
                    }

                case 0x6C:
                    {
                        regIXY = (regIXY & 0xff00) | (regIXY >> 8);
                        break;
                    }

                case 0x6D:
                    {
                        break;
                    }

                case 0x6E:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        regL = MemIoImpl.Peek8(memptr);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x6F:
                    {
                        regIXY = (regIXY & 0xff00) | regA;
                        break;
                    }

                case 0x70:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        MemIoImpl.Poke8(memptr, regB);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x71:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        MemIoImpl.Poke8(memptr, regC);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x72:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        MemIoImpl.Poke8(memptr, regD);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x73:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        MemIoImpl.Poke8(memptr, regE);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x74:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        MemIoImpl.Poke8(memptr, regH);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x75:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        MemIoImpl.Poke8(memptr, regL);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x77:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        MemIoImpl.Poke8(memptr, regA);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x7C:
                    {
                        regA = regIXY >> 8;
                        break;
                    }

                case 0x7D:
                    {
                        regA = regIXY & 0xff;
                        break;
                    }

                case 0x7E:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        regA = MemIoImpl.Peek8(memptr);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x84:
                    {
                        Add(regIXY >> 8);
                        break;
                    }

                case 0x85:
                    {
                        Add(regIXY & 0xff);
                        break;
                    }

                case 0x86:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        Add(MemIoImpl.Peek8(memptr));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x8C:
                    {
                        Adc(regIXY >> 8);
                        break;
                    }

                case 0x8D:
                    {
                        Adc(regIXY & 0xff);
                        break;
                    }

                case 0x8E:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        Adc(MemIoImpl.Peek8(memptr));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x94:
                    {
                        Sub(regIXY >> 8);
                        break;
                    }

                case 0x95:
                    {
                        Sub(regIXY & 0xff);
                        break;
                    }

                case 0x96:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        Sub(MemIoImpl.Peek8(memptr));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x9C:
                    {
                        Sbc(regIXY >> 8);
                        break;
                    }

                case 0x9D:
                    {
                        Sbc(regIXY & 0xff);
                        break;
                    }

                case 0x9E:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        Sbc(MemIoImpl.Peek8(memptr));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xA4:
                    {
                        And(regIXY >> 8);
                        break;
                    }

                case 0xA5:
                    {
                        And(regIXY & 0xff);
                        break;
                    }

                case 0xA6:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        And(MemIoImpl.Peek8(memptr));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xAC:
                    {
                        Xor(regIXY >> 8);
                        break;
                    }

                case 0xAD:
                    {
                        Xor(regIXY & 0xff);
                        break;
                    }

                case 0xAE:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        Xor(MemIoImpl.Peek8(memptr));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xB4:
                    {
                        Or(regIXY >> 8);
                        break;
                    }

                case 0xB5:
                    {
                        Or(regIXY & 0xff);
                        break;
                    }

                case 0xB6:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        Or(MemIoImpl.Peek8(memptr));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xBC:
                    {
                        Cp(regIXY >> 8);
                        break;
                    }

                case 0xBD:
                    {
                        Cp(regIXY & 0xff);
                        break;
                    }

                case 0xBE:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        MemIoImpl.AddressOnBus(regPC, 5);
                        Cp(MemIoImpl.Peek8(memptr));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xCB:
                    {
                        memptr = (regIXY + (byte)MemIoImpl.Peek8(regPC)) & 0xffff;
                        regPC = (regPC + 1) & 0xffff;
                        opCode = MemIoImpl.Peek8(regPC);
                        MemIoImpl.AddressOnBus(regPC, 2);
                        regPC = (regPC + 1) & 0xffff;
                        DecodeDDFDCB(opCode, memptr);
                        break;
                    }

                case 0xDD:
                    {
                        prefixOpcode = 0xDD;
                        break;
                    }

                case 0xE1:
                    {
                        regIXY = Pop();
                        break;
                    }

                case 0xE3:
                    {
                        int work16 = regIXY;
                        regIXY = MemIoImpl.Peek16(regSP);
                        MemIoImpl.AddressOnBus((regSP + 1) & 0xffff, 1);
                        MemIoImpl.Poke8((regSP + 1) & 0xffff, work16 >> 8);
                        MemIoImpl.Poke8(regSP, work16);
                        MemIoImpl.AddressOnBus(regSP, 2);
                        memptr = regIXY;
                        break;
                    }

                case 0xE5:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        Push(regIXY);
                        break;
                    }

                case 0xE9:
                    {
                        regPC = regIXY;
                        break;
                    }

                case 0xED:
                    {
                        prefixOpcode = 0xED;
                        break;
                    }

                case 0xF9:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        regSP = regIXY;
                        break;
                    }

                case 0xFD:
                    {
                        prefixOpcode = 0xFD;
                        break;
                    }

                default:
                    {
                        if (breakpointAt[regPC])
                        {
                            opCode = NotifyImpl.Breakpoint(regPC, opCode);
                        }

                        DecodeOpcode(opCode);
                        break;
                    }
            }

            return regIXY;
        }

        private void DecodeDDFDCB(int opCode, int address)
        {
            switch (opCode)
            {
                case 0x00:
                case 0x01:
                case 0x02:
                case 0x03:
                case 0x04:
                case 0x05:
                case 0x06:
                case 0x07:
                    {
                        int work8 = Rlc(MemIoImpl.Peek8(address));
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x08:
                case 0x09:
                case 0x0A:
                case 0x0B:
                case 0x0C:
                case 0x0D:
                case 0x0E:
                case 0x0F:
                    {
                        int work8 = Rrc(MemIoImpl.Peek8(address));
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x10:
                case 0x11:
                case 0x12:
                case 0x13:
                case 0x14:
                case 0x15:
                case 0x16:
                case 0x17:
                    {
                        int work8 = Rl(MemIoImpl.Peek8(address));
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x18:
                case 0x19:
                case 0x1A:
                case 0x1B:
                case 0x1C:
                case 0x1D:
                case 0x1E:
                case 0x1F:
                    {
                        int work8 = Rr(MemIoImpl.Peek8(address));
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x20:
                case 0x21:
                case 0x22:
                case 0x23:
                case 0x24:
                case 0x25:
                case 0x26:
                case 0x27:
                    {
                        int work8 = Sla(MemIoImpl.Peek8(address));
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x28:
                case 0x29:
                case 0x2A:
                case 0x2B:
                case 0x2C:
                case 0x2D:
                case 0x2E:
                case 0x2F:
                    {
                        int work8 = Sra(MemIoImpl.Peek8(address));
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x30:
                case 0x31:
                case 0x32:
                case 0x33:
                case 0x34:
                case 0x35:
                case 0x36:
                case 0x37:
                    {
                        int work8 = Sll(MemIoImpl.Peek8(address));
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x38:
                case 0x39:
                case 0x3A:
                case 0x3B:
                case 0x3C:
                case 0x3D:
                case 0x3E:
                case 0x3F:
                    {
                        int work8 = Srl(MemIoImpl.Peek8(address));
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x40:
                case 0x41:
                case 0x42:
                case 0x43:
                case 0x44:
                case 0x45:
                case 0x46:
                case 0x47:
                    {
                        Bit(0x01, MemIoImpl.Peek8(address));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((address >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(address, 1);
                        break;
                    }

                case 0x48:
                case 0x49:
                case 0x4A:
                case 0x4B:
                case 0x4C:
                case 0x4D:
                case 0x4E:
                case 0x4F:
                    {
                        Bit(0x02, MemIoImpl.Peek8(address));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((address >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(address, 1);
                        break;
                    }

                case 0x50:
                case 0x51:
                case 0x52:
                case 0x53:
                case 0x54:
                case 0x55:
                case 0x56:
                case 0x57:
                    {
                        Bit(0x04, MemIoImpl.Peek8(address));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((address >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(address, 1);
                        break;
                    }

                case 0x58:
                case 0x59:
                case 0x5A:
                case 0x5B:
                case 0x5C:
                case 0x5D:
                case 0x5E:
                case 0x5F:
                    {
                        Bit(0x08, MemIoImpl.Peek8(address));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((address >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(address, 1);
                        break;
                    }

                case 0x60:
                case 0x61:
                case 0x62:
                case 0x63:
                case 0x64:
                case 0x65:
                case 0x66:
                case 0x67:
                    {
                        Bit(0x10, MemIoImpl.Peek8(address));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((address >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(address, 1);
                        break;
                    }

                case 0x68:
                case 0x69:
                case 0x6A:
                case 0x6B:
                case 0x6C:
                case 0x6D:
                case 0x6E:
                case 0x6F:
                    {
                        Bit(0x20, MemIoImpl.Peek8(address));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((address >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(address, 1);
                        break;
                    }

                case 0x70:
                case 0x71:
                case 0x72:
                case 0x73:
                case 0x74:
                case 0x75:
                case 0x76:
                case 0x77:
                    {
                        Bit(0x40, MemIoImpl.Peek8(address));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((address >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(address, 1);
                        break;
                    }

                case 0x78:
                case 0x79:
                case 0x7A:
                case 0x7B:
                case 0x7C:
                case 0x7D:
                case 0x7E:
                case 0x7F:
                    {
                        Bit(0x80, MemIoImpl.Peek8(address));
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZHP_MASK) | ((address >> 8) & FLAG_53_MASK);
                        MemIoImpl.AddressOnBus(address, 1);
                        break;
                    }

                case 0x80:
                case 0x81:
                case 0x82:
                case 0x83:
                case 0x84:
                case 0x85:
                case 0x86:
                case 0x87:
                    {
                        int work8 = MemIoImpl.Peek8(address) & 0xFE;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x88:
                case 0x89:
                case 0x8A:
                case 0x8B:
                case 0x8C:
                case 0x8D:
                case 0x8E:
                case 0x8F:
                    {
                        int work8 = MemIoImpl.Peek8(address) & 0xFD;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x90:
                case 0x91:
                case 0x92:
                case 0x93:
                case 0x94:
                case 0x95:
                case 0x96:
                case 0x97:
                    {
                        int work8 = MemIoImpl.Peek8(address) & 0xFB;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0x98:
                case 0x99:
                case 0x9A:
                case 0x9B:
                case 0x9C:
                case 0x9D:
                case 0x9E:
                case 0x9F:
                    {
                        int work8 = MemIoImpl.Peek8(address) & 0xF7;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xA0:
                case 0xA1:
                case 0xA2:
                case 0xA3:
                case 0xA4:
                case 0xA5:
                case 0xA6:
                case 0xA7:
                    {
                        int work8 = MemIoImpl.Peek8(address) & 0xEF;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xA8:
                case 0xA9:
                case 0xAA:
                case 0xAB:
                case 0xAC:
                case 0xAD:
                case 0xAE:
                case 0xAF:
                    {
                        int work8 = MemIoImpl.Peek8(address) & 0xDF;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xB0:
                case 0xB1:
                case 0xB2:
                case 0xB3:
                case 0xB4:
                case 0xB5:
                case 0xB6:
                case 0xB7:
                    {
                        int work8 = MemIoImpl.Peek8(address) & 0xBF;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xB8:
                case 0xB9:
                case 0xBA:
                case 0xBB:
                case 0xBC:
                case 0xBD:
                case 0xBE:
                case 0xBF:
                    {
                        int work8 = MemIoImpl.Peek8(address) & 0x7F;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xC0:
                case 0xC1:
                case 0xC2:
                case 0xC3:
                case 0xC4:
                case 0xC5:
                case 0xC6:
                case 0xC7:
                    {
                        int work8 = MemIoImpl.Peek8(address) | 0x01;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xC8:
                case 0xC9:
                case 0xCA:
                case 0xCB:
                case 0xCC:
                case 0xCD:
                case 0xCE:
                case 0xCF:
                    {
                        int work8 = MemIoImpl.Peek8(address) | 0x02;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xD0:
                case 0xD1:
                case 0xD2:
                case 0xD3:
                case 0xD4:
                case 0xD5:
                case 0xD6:
                case 0xD7:
                    {
                        int work8 = MemIoImpl.Peek8(address) | 0x04;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xD8:
                case 0xD9:
                case 0xDA:
                case 0xDB:
                case 0xDC:
                case 0xDD:
                case 0xDE:
                case 0xDF:
                    {
                        int work8 = MemIoImpl.Peek8(address) | 0x08;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xE0:
                case 0xE1:
                case 0xE2:
                case 0xE3:
                case 0xE4:
                case 0xE5:
                case 0xE6:
                case 0xE7:
                    {
                        int work8 = MemIoImpl.Peek8(address) | 0x10;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xE8:
                case 0xE9:
                case 0xEA:
                case 0xEB:
                case 0xEC:
                case 0xED:
                case 0xEE:
                case 0xEF:
                    {
                        int work8 = MemIoImpl.Peek8(address) | 0x20;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xF0:
                case 0xF1:
                case 0xF2:
                case 0xF3:
                case 0xF4:
                case 0xF5:
                case 0xF6:
                case 0xF7:
                    {
                        int work8 = MemIoImpl.Peek8(address) | 0x40;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }

                case 0xF8:
                case 0xF9:
                case 0xFA:
                case 0xFB:
                case 0xFC:
                case 0xFD:
                case 0xFE:
                case 0xFF:
                    {
                        int work8 = MemIoImpl.Peek8(address) | 0x80;
                        MemIoImpl.AddressOnBus(address, 1);
                        MemIoImpl.Poke8(address, work8);
                        CopyToRegister(opCode, work8);
                        break;
                    }
            }
        }

        private void DecodeED(int opCode)
        {
            switch (opCode)
            {
                case 0x40:
                    {
                        memptr = GetRegBC();
                        regB = MemIoImpl.InPort(memptr++);
                        sz5h3pnFlags = sz53pn_addTable[regB];
                        flagQ = true;
                        break;
                    }

                case 0x41:
                    {
                        memptr = GetRegBC();
                        MemIoImpl.OutPort(memptr++, regB);
                        break;
                    }

                case 0x42:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        Sbc16(GetRegBC());
                        break;
                    }

                case 0x43:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        MemIoImpl.Poke16(memptr++, GetRegBC());
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x44:
                case 0x4C:
                case 0x54:
                case 0x5C:
                case 0x64:
                case 0x6C:
                case 0x74:
                case 0x7C:
                    {
                        int aux = regA;
                        regA = 0;
                        Sub(aux);
                        break;
                    }

                case 0x45:
                case 0x4D:
                case 0x55:
                case 0x5D:
                case 0x65:
                case 0x6D:
                case 0x75:
                case 0x7D:
                    {
                        ffIFF1 = ffIFF2;
                        regPC = memptr = Pop();
                        break;
                    }

                case 0x46:
                case 0x4E:
                case 0x66:
                case 0x6E:
                    {
                        SetIM(IntMode.IM0);
                        break;
                    }

                case 0x47:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        regI = regA;
                        break;
                    }

                case 0x48:
                    {
                        memptr = GetRegBC();
                        regC = MemIoImpl.InPort(memptr++);
                        sz5h3pnFlags = sz53pn_addTable[regC];
                        flagQ = true;
                        break;
                    }

                case 0x49:
                    {
                        memptr = GetRegBC();
                        MemIoImpl.OutPort(memptr++, regC);
                        break;
                    }

                case 0x4A:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        Adc16(GetRegBC());
                        break;
                    }

                case 0x4B:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        SetRegBC(MemIoImpl.Peek16(memptr++));
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x4F:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        SetRegR(regA);
                        break;
                    }

                case 0x50:
                    {
                        memptr = GetRegBC();
                        regD = MemIoImpl.InPort(memptr++);
                        sz5h3pnFlags = sz53pn_addTable[regD];
                        flagQ = true;
                        break;
                    }

                case 0x51:
                    {
                        memptr = GetRegBC();
                        MemIoImpl.OutPort(memptr++, regD);
                        break;
                    }

                case 0x52:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        Sbc16(GetRegDE());
                        break;
                    }

                case 0x53:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        MemIoImpl.Poke16(memptr++, GetRegDE());
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x56:
                case 0x76:
                    {
                        SetIM(IntMode.IM1);
                        break;
                    }

                case 0x57:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        regA = regI;
                        sz5h3pnFlags = sz53n_addTable[regA];
                        if (ffIFF2 && !MemIoImpl.IsActiveINT())
                        {
                            sz5h3pnFlags |= PARITY_MASK;
                        }

                        flagQ = true;
                        break;
                    }

                case 0x58:
                    {
                        memptr = GetRegBC();
                        regE = MemIoImpl.InPort(memptr++);
                        sz5h3pnFlags = sz53pn_addTable[regE];
                        flagQ = true;
                        break;
                    }

                case 0x59:
                    {
                        memptr = GetRegBC();
                        MemIoImpl.OutPort(memptr++, regE);
                        break;
                    }

                case 0x5A:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        Adc16(GetRegDE());
                        break;
                    }

                case 0x5B:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        SetRegDE(MemIoImpl.Peek16(memptr++));
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x5E:
                case 0x7E:
                    {
                        SetIM(IntMode.IM2);
                        break;
                    }

                case 0x5F:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        regA = GetRegR();
                        sz5h3pnFlags = sz53n_addTable[regA];
                        if (ffIFF2 && !MemIoImpl.IsActiveINT())
                        {
                            sz5h3pnFlags |= PARITY_MASK;
                        }

                        flagQ = true;
                        break;
                    }

                case 0x60:
                    {
                        memptr = GetRegBC();
                        regH = MemIoImpl.InPort(memptr++);
                        sz5h3pnFlags = sz53pn_addTable[regH];
                        flagQ = true;
                        break;
                    }

                case 0x61:
                    {
                        memptr = GetRegBC();
                        MemIoImpl.OutPort(memptr++, regH);
                        break;
                    }

                case 0x62:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        Sbc16(GetRegHL());
                        break;
                    }

                case 0x63:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        MemIoImpl.Poke16(memptr++, GetRegHL());
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x67:
                    {
                        Rrd();
                        break;
                    }

                case 0x68:
                    {
                        memptr = GetRegBC();
                        regL = MemIoImpl.InPort(memptr++);
                        sz5h3pnFlags = sz53pn_addTable[regL];
                        flagQ = true;
                        break;
                    }

                case 0x69:
                    {
                        memptr = GetRegBC();
                        MemIoImpl.OutPort(memptr++, regL);
                        break;
                    }

                case 0x6A:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        Adc16(GetRegHL());
                        break;
                    }

                case 0x6B:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        SetRegHL(MemIoImpl.Peek16(memptr++));
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x6F:
                    {
                        Rld();
                        break;
                    }

                case 0x70:
                    {
                        memptr = GetRegBC();
                        int inPort = MemIoImpl.InPort(memptr++);
                        sz5h3pnFlags = sz53pn_addTable[inPort];
                        flagQ = true;
                        break;
                    }

                case 0x71:
                    {
                        memptr = GetRegBC();
                        MemIoImpl.OutPort(memptr++, 0x00);
                        break;
                    }

                case 0x72:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        Sbc16(regSP);
                        break;
                    }

                case 0x73:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        MemIoImpl.Poke16(memptr++, regSP);
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x78:
                    {
                        memptr = GetRegBC();
                        regA = MemIoImpl.InPort(memptr++);
                        sz5h3pnFlags = sz53pn_addTable[regA];
                        flagQ = true;
                        break;
                    }

                case 0x79:
                    {
                        memptr = GetRegBC();
                        MemIoImpl.OutPort(memptr++, regA);
                        break;
                    }

                case 0x7A:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        Adc16(regSP);
                        break;
                    }

                case 0x7B:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        regSP = MemIoImpl.Peek16(memptr++);
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0xA0:
                    {
                        Ldi();
                        break;
                    }

                case 0xA1:
                    {
                        Cpi();
                        break;
                    }

                case 0xA2:
                    {
                        Ini();
                        break;
                    }

                case 0xA3:
                    {
                        Outi();
                        break;
                    }

                case 0xA8:
                    {
                        Ldd();
                        break;
                    }

                case 0xA9:
                    {
                        Cpd();
                        break;
                    }

                case 0xAA:
                    {
                        Ind();
                        break;
                    }

                case 0xAB:
                    {
                        Outd();
                        break;
                    }

                case 0xB0:
                    {
                        Ldi();
                        if ((sz5h3pnFlags & PARITY_MASK) == PARITY_MASK)
                        {
                            regPC = (regPC - 2) & 0xffff;
                            memptr = regPC + 1;
                            MemIoImpl.AddressOnBus((GetRegDE() - 1) & 0xffff, 5);
                        }

                        break;
                    }

                case 0xB1:
                    {
                        Cpi();
                        if ((sz5h3pnFlags & PARITY_MASK) == PARITY_MASK && (sz5h3pnFlags & ZERO_MASK) == 0)
                        {
                            regPC = (regPC - 2) & 0xffff;
                            memptr = regPC + 1;
                            MemIoImpl.AddressOnBus((GetRegHL() - 1) & 0xffff, 5);
                        }

                        break;
                    }

                case 0xB2:
                    {
                        Ini();
                        if (regB != 0)
                        {
                            regPC = (regPC - 2) & 0xffff;
                            MemIoImpl.AddressOnBus((GetRegHL() - 1) & 0xffff, 5);
                        }

                        break;
                    }

                case 0xB3:
                    {
                        Outi();
                        if (regB != 0)
                        {
                            regPC = (regPC - 2) & 0xffff;
                            MemIoImpl.AddressOnBus(GetRegBC(), 5);
                        }

                        break;
                    }

                case 0xB8:
                    {
                        Ldd();
                        if ((sz5h3pnFlags & PARITY_MASK) == PARITY_MASK)
                        {
                            regPC = (regPC - 2) & 0xffff;
                            memptr = regPC + 1;
                            MemIoImpl.AddressOnBus((GetRegDE() + 1) & 0xffff, 5);
                        }

                        break;
                    }

                case 0xB9:
                    {
                        Cpd();
                        if ((sz5h3pnFlags & PARITY_MASK) == PARITY_MASK && (sz5h3pnFlags & ZERO_MASK) == 0)
                        {
                            regPC = (regPC - 2) & 0xffff;
                            memptr = regPC + 1;
                            MemIoImpl.AddressOnBus((GetRegHL() + 1) & 0xffff, 5);
                        }

                        break;
                    }

                case 0xBA:
                    {
                        Ind();
                        if (regB != 0)
                        {
                            regPC = (regPC - 2) & 0xffff;
                            MemIoImpl.AddressOnBus((GetRegHL() + 1) & 0xffff, 5);
                        }

                        break;
                    }

                case 0xBB:
                    {
                        Outd();
                        if (regB != 0)
                        {
                            regPC = (regPC - 2) & 0xffff;
                            MemIoImpl.AddressOnBus(GetRegBC(), 5);
                        }

                        break;
                    }

                case 0xDD:
                    prefixOpcode = 0xDD;
                    break;

                case 0xED:
                    prefixOpcode = 0xED;
                    break;

                case 0xFD:
                    prefixOpcode = 0xFD;
                    break;

                default:
                    {
                        break;
                    }
            }
        }

        private void DecodeOpcode(int opCode)
        {
            switch (opCode)
            {
                case 0x01:
                    {
                        SetRegBC(MemIoImpl.Peek16(regPC));
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x02:
                    {
                        MemIoImpl.Poke8(GetRegBC(), regA);
                        memptr = (regA << 8) | ((regC + 1) & 0xff);
                        break;
                    }

                case 0x03:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        IncRegBC();
                        break;
                    }

                case 0x04:
                    {
                        regB = Inc8(regB);
                        break;
                    }

                case 0x05:
                    {
                        regB = Dec8(regB);
                        break;
                    }

                case 0x06:
                    {
                        regB = MemIoImpl.Peek8(regPC);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x07:
                    {
                        carryFlag = (regA > 0x7f);
                        regA = (regA << 1) & 0xff;
                        if (carryFlag)
                        {
                            regA |= CARRY_MASK;
                        }

                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZP_MASK) | (regA & FLAG_53_MASK);
                        flagQ = true;
                        break;
                    }

                case 0x08:
                    {
                        int work8 = regA;
                        regA = regAx;
                        regAx = work8;
                        work8 = GetFlags();
                        SetFlags(regFx);
                        regFx = work8;
                        break;
                    }

                case 0x09:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        SetRegHL(Add16(GetRegHL(), GetRegBC()));
                        break;
                    }

                case 0x0A:
                    {
                        memptr = GetRegBC();
                        regA = MemIoImpl.Peek8(memptr++);
                        break;
                    }

                case 0x0B:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        DecRegBC();
                        break;
                    }

                case 0x0C:
                    {
                        regC = Inc8(regC);
                        break;
                    }

                case 0x0D:
                    {
                        regC = Dec8(regC);
                        break;
                    }

                case 0x0E:
                    {
                        regC = MemIoImpl.Peek8(regPC);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x0F:
                    {
                        carryFlag = (regA & CARRY_MASK) != 0;
                        regA >>= 1;
                        if (carryFlag)
                        {
                            regA |= SIGN_MASK;
                        }

                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZP_MASK) | (regA & FLAG_53_MASK);
                        flagQ = true;
                        break;
                    }

                case 0x10:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        byte offset = (byte)MemIoImpl.Peek8(regPC);
                        regB--;
                        if (regB != 0)
                        {
                            regB &= 0xff;
                            MemIoImpl.AddressOnBus(regPC, 5);
                            regPC = memptr = (regPC + offset + 1) & 0xffff;
                        }
                        else
                        {
                            regPC = (regPC + 1) & 0xffff;
                        }

                        break;
                    }

                case 0x11:
                    {
                        SetRegDE(MemIoImpl.Peek16(regPC));
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x12:
                    {
                        MemIoImpl.Poke8(GetRegDE(), regA);
                        memptr = (regA << 8) | ((regE + 1) & 0xff);
                        break;
                    }

                case 0x13:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        IncRegDE();
                        break;
                    }

                case 0x14:
                    {
                        regD = Inc8(regD);
                        break;
                    }

                case 0x15:
                    {
                        regD = Dec8(regD);
                        break;
                    }

                case 0x16:
                    {
                        regD = MemIoImpl.Peek8(regPC);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x17:
                    {
                        bool oldCarry = carryFlag;
                        carryFlag = (regA > 0x7f);
                        regA = (regA << 1) & 0xff;
                        if (oldCarry)
                        {
                            regA |= CARRY_MASK;
                        }

                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZP_MASK) | (regA & FLAG_53_MASK);
                        flagQ = true;
                        break;
                    }

                case 0x18:
                    {
                        byte offset = (byte)MemIoImpl.Peek8(regPC);
                        MemIoImpl.AddressOnBus(regPC, 5);
                        regPC = memptr = (regPC + offset + 1) & 0xffff;
                        break;
                    }

                case 0x19:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        SetRegHL(Add16(GetRegHL(), GetRegDE()));
                        break;
                    }

                case 0x1A:
                    {
                        memptr = GetRegDE();
                        regA = MemIoImpl.Peek8(memptr++);
                        break;
                    }

                case 0x1B:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        DecRegDE();
                        break;
                    }

                case 0x1C:
                    {
                        regE = Inc8(regE);
                        break;
                    }

                case 0x1D:
                    {
                        regE = Dec8(regE);
                        break;
                    }

                case 0x1E:
                    {
                        regE = MemIoImpl.Peek8(regPC);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x1F:
                    {
                        bool oldCarry = carryFlag;
                        carryFlag = (regA & CARRY_MASK) != 0;
                        regA >>= 1;
                        if (oldCarry)
                        {
                            regA |= SIGN_MASK;
                        }

                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZP_MASK) | (regA & FLAG_53_MASK);
                        flagQ = true;
                        break;
                    }

                case 0x20:
                    {
                        byte offset = (byte)MemIoImpl.Peek8(regPC);
                        if ((sz5h3pnFlags & ZERO_MASK) == 0)
                        {
                            MemIoImpl.AddressOnBus(regPC, 5);
                            regPC += offset;
                            memptr = regPC + 1;
                        }

                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x21:
                    {
                        SetRegHL(MemIoImpl.Peek16(regPC));
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x22:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        MemIoImpl.Poke16(memptr++, GetRegHL());
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x23:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        IncRegHL();
                        break;
                    }

                case 0x24:
                    {
                        regH = Inc8(regH);
                        break;
                    }

                case 0x25:
                    {
                        regH = Dec8(regH);
                        break;
                    }

                case 0x26:
                    {
                        regH = MemIoImpl.Peek8(regPC);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x27:
                    {
                        Daa();
                        break;
                    }

                case 0x28:
                    {
                        byte offset = (byte)MemIoImpl.Peek8(regPC);
                        if ((sz5h3pnFlags & ZERO_MASK) != 0)
                        {
                            MemIoImpl.AddressOnBus(regPC, 5);
                            regPC += offset;
                            memptr = regPC + 1;
                        }

                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x29:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        int work16 = GetRegHL();
                        SetRegHL(Add16(work16, work16));
                        break;
                    }

                case 0x2A:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        SetRegHL(MemIoImpl.Peek16(memptr++));
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x2B:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        DecRegHL();
                        break;
                    }

                case 0x2C:
                    {
                        regL = Inc8(regL);
                        break;
                    }

                case 0x2D:
                    {
                        regL = Dec8(regL);
                        break;
                    }

                case 0x2E:
                    {
                        regL = MemIoImpl.Peek8(regPC);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x2F:
                    {
                        regA ^= 0xff;
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZP_MASK) | HALFCARRY_MASK | (regA & FLAG_53_MASK) | ADDSUB_MASK;
                        flagQ = true;
                        break;
                    }

                case 0x30:
                    {
                        byte offset = (byte)MemIoImpl.Peek8(regPC);
                        if (!carryFlag)
                        {
                            MemIoImpl.AddressOnBus(regPC, 5);
                            regPC += offset;
                            memptr = regPC + 1;
                        }

                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x31:
                    {
                        regSP = MemIoImpl.Peek16(regPC);
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x32:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        MemIoImpl.Poke8(memptr, regA);
                        memptr = (regA << 8) | ((memptr + 1) & 0xff);
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x33:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        regSP = (regSP + 1) & 0xffff;
                        break;
                    }

                case 0x34:
                    {
                        int work16 = GetRegHL();
                        int work8 = Inc8(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x35:
                    {
                        int work16 = GetRegHL();
                        int work8 = Dec8(MemIoImpl.Peek8(work16));
                        MemIoImpl.AddressOnBus(work16, 1);
                        MemIoImpl.Poke8(work16, work8);
                        break;
                    }

                case 0x36:
                    {
                        MemIoImpl.Poke8(GetRegHL(), MemIoImpl.Peek8(regPC));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x37:
                    {
                        int regQ = lastFlagQ ? sz5h3pnFlags : 0;
                        carryFlag = true;
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZP_MASK) | (((regQ ^ sz5h3pnFlags) | regA) & FLAG_53_MASK);
                        flagQ = true;
                        break;
                    }

                case 0x38:
                    {
                        byte offset = (byte)MemIoImpl.Peek8(regPC);
                        if (carryFlag)
                        {
                            MemIoImpl.AddressOnBus(regPC, 5);
                            regPC += offset;
                            memptr = regPC + 1;
                        }

                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x39:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 7);
                        SetRegHL(Add16(GetRegHL(), regSP));
                        break;
                    }

                case 0x3A:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        regA = MemIoImpl.Peek8(memptr++);
                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0x3B:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 2);
                        regSP = (regSP - 1) & 0xffff;
                        break;
                    }

                case 0x3C:
                    {
                        regA = Inc8(regA);
                        break;
                    }

                case 0x3D:
                    {
                        regA = Dec8(regA);
                        break;
                    }

                case 0x3E:
                    {
                        regA = MemIoImpl.Peek8(regPC);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0x3F:
                    {
                        int regQ = lastFlagQ ? sz5h3pnFlags : 0;
                        sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZP_MASK) | (((regQ ^ sz5h3pnFlags) | regA) & FLAG_53_MASK);
                        if (carryFlag)
                        {
                            sz5h3pnFlags |= HALFCARRY_MASK;
                        }

                        carryFlag = !carryFlag;
                        flagQ = true;
                        break;
                    }

                case 0x41:
                    {
                        regB = regC;
                        break;
                    }

                case 0x42:
                    {
                        regB = regD;
                        break;
                    }

                case 0x43:
                    {
                        regB = regE;
                        break;
                    }

                case 0x44:
                    {
                        regB = regH;
                        break;
                    }

                case 0x45:
                    {
                        regB = regL;
                        break;
                    }

                case 0x46:
                    {
                        regB = MemIoImpl.Peek8(GetRegHL());
                        break;
                    }

                case 0x47:
                    {
                        regB = regA;
                        break;
                    }

                case 0x48:
                    {
                        regC = regB;
                        break;
                    }

                case 0x4A:
                    {
                        regC = regD;
                        break;
                    }

                case 0x4B:
                    {
                        regC = regE;
                        break;
                    }

                case 0x4C:
                    {
                        regC = regH;
                        break;
                    }

                case 0x4D:
                    {
                        regC = regL;
                        break;
                    }

                case 0x4E:
                    {
                        regC = MemIoImpl.Peek8(GetRegHL());
                        break;
                    }

                case 0x4F:
                    {
                        regC = regA;
                        break;
                    }

                case 0x50:
                    {
                        regD = regB;
                        break;
                    }

                case 0x51:
                    {
                        regD = regC;
                        break;
                    }

                case 0x53:
                    {
                        regD = regE;
                        break;
                    }

                case 0x54:
                    {
                        regD = regH;
                        break;
                    }

                case 0x55:
                    {
                        regD = regL;
                        break;
                    }

                case 0x56:
                    {
                        regD = MemIoImpl.Peek8(GetRegHL());
                        break;
                    }

                case 0x57:
                    {
                        regD = regA;
                        break;
                    }

                case 0x58:
                    {
                        regE = regB;
                        break;
                    }

                case 0x59:
                    {
                        regE = regC;
                        break;
                    }

                case 0x5A:
                    {
                        regE = regD;
                        break;
                    }

                case 0x5C:
                    {
                        regE = regH;
                        break;
                    }

                case 0x5D:
                    {
                        regE = regL;
                        break;
                    }

                case 0x5E:
                    {
                        regE = MemIoImpl.Peek8(GetRegHL());
                        break;
                    }

                case 0x5F:
                    {
                        regE = regA;
                        break;
                    }

                case 0x60:
                    {
                        regH = regB;
                        break;
                    }

                case 0x61:
                    {
                        regH = regC;
                        break;
                    }

                case 0x62:
                    {
                        regH = regD;
                        break;
                    }

                case 0x63:
                    {
                        regH = regE;
                        break;
                    }

                case 0x65:
                    {
                        regH = regL;
                        break;
                    }

                case 0x66:
                    {
                        regH = MemIoImpl.Peek8(GetRegHL());
                        break;
                    }

                case 0x67:
                    {
                        regH = regA;
                        break;
                    }

                case 0x68:
                    {
                        regL = regB;
                        break;
                    }

                case 0x69:
                    {
                        regL = regC;
                        break;
                    }

                case 0x6A:
                    {
                        regL = regD;
                        break;
                    }

                case 0x6B:
                    {
                        regL = regE;
                        break;
                    }

                case 0x6C:
                    {
                        regL = regH;
                        break;
                    }

                case 0x6E:
                    {
                        regL = MemIoImpl.Peek8(GetRegHL());
                        break;
                    }

                case 0x6F:
                    {
                        regL = regA;
                        break;
                    }

                case 0x70:
                    {
                        MemIoImpl.Poke8(GetRegHL(), regB);
                        break;
                    }

                case 0x71:
                    {
                        MemIoImpl.Poke8(GetRegHL(), regC);
                        break;
                    }

                case 0x72:
                    {
                        MemIoImpl.Poke8(GetRegHL(), regD);
                        break;
                    }

                case 0x73:
                    {
                        MemIoImpl.Poke8(GetRegHL(), regE);
                        break;
                    }

                case 0x74:
                    {
                        MemIoImpl.Poke8(GetRegHL(), regH);
                        break;
                    }

                case 0x75:
                    {
                        MemIoImpl.Poke8(GetRegHL(), regL);
                        break;
                    }

                case 0x76:
                    {
                        regPC = (regPC - 1) & 0xffff;
                        halted = true;
                        break;
                    }

                case 0x77:
                    {
                        MemIoImpl.Poke8(GetRegHL(), regA);
                        break;
                    }

                case 0x78:
                    {
                        regA = regB;
                        break;
                    }

                case 0x79:
                    {
                        regA = regC;
                        break;
                    }

                case 0x7A:
                    {
                        regA = regD;
                        break;
                    }

                case 0x7B:
                    {
                        regA = regE;
                        break;
                    }

                case 0x7C:
                    {
                        regA = regH;
                        break;
                    }

                case 0x7D:
                    {
                        regA = regL;
                        break;
                    }

                case 0x7E:
                    {
                        regA = MemIoImpl.Peek8(GetRegHL());
                        break;
                    }

                case 0x80:
                    {
                        Add(regB);
                        break;
                    }

                case 0x81:
                    {
                        Add(regC);
                        break;
                    }

                case 0x82:
                    {
                        Add(regD);
                        break;
                    }

                case 0x83:
                    {
                        Add(regE);
                        break;
                    }

                case 0x84:
                    {
                        Add(regH);
                        break;
                    }

                case 0x85:
                    {
                        Add(regL);
                        break;
                    }

                case 0x86:
                    {
                        Add(MemIoImpl.Peek8(GetRegHL()));
                        break;
                    }

                case 0x87:
                    {
                        Add(regA);
                        break;
                    }

                case 0x88:
                    {
                        Adc(regB);
                        break;
                    }

                case 0x89:
                    {
                        Adc(regC);
                        break;
                    }

                case 0x8A:
                    {
                        Adc(regD);
                        break;
                    }

                case 0x8B:
                    {
                        Adc(regE);
                        break;
                    }

                case 0x8C:
                    {
                        Adc(regH);
                        break;
                    }

                case 0x8D:
                    {
                        Adc(regL);
                        break;
                    }

                case 0x8E:
                    {
                        Adc(MemIoImpl.Peek8(GetRegHL()));
                        break;
                    }

                case 0x8F:
                    {
                        Adc(regA);
                        break;
                    }

                case 0x90:
                    {
                        Sub(regB);
                        break;
                    }

                case 0x91:
                    {
                        Sub(regC);
                        break;
                    }

                case 0x92:
                    {
                        Sub(regD);
                        break;
                    }

                case 0x93:
                    {
                        Sub(regE);
                        break;
                    }

                case 0x94:
                    {
                        Sub(regH);
                        break;
                    }

                case 0x95:
                    {
                        Sub(regL);
                        break;
                    }

                case 0x96:
                    {
                        Sub(MemIoImpl.Peek8(GetRegHL()));
                        break;
                    }

                case 0x97:
                    {
                        Sub(regA);
                        break;
                    }

                case 0x98:
                    {
                        Sbc(regB);
                        break;
                    }

                case 0x99:
                    {
                        Sbc(regC);
                        break;
                    }

                case 0x9A:
                    {
                        Sbc(regD);
                        break;
                    }

                case 0x9B:
                    {
                        Sbc(regE);
                        break;
                    }

                case 0x9C:
                    {
                        Sbc(regH);
                        break;
                    }

                case 0x9D:
                    {
                        Sbc(regL);
                        break;
                    }

                case 0x9E:
                    {
                        Sbc(MemIoImpl.Peek8(GetRegHL()));
                        break;
                    }

                case 0x9F:
                    {
                        Sbc(regA);
                        break;
                    }

                case 0xA0:
                    {
                        And(regB);
                        break;
                    }

                case 0xA1:
                    {
                        And(regC);
                        break;
                    }

                case 0xA2:
                    {
                        And(regD);
                        break;
                    }

                case 0xA3:
                    {
                        And(regE);
                        break;
                    }

                case 0xA4:
                    {
                        And(regH);
                        break;
                    }

                case 0xA5:
                    {
                        And(regL);
                        break;
                    }

                case 0xA6:
                    {
                        And(MemIoImpl.Peek8(GetRegHL()));
                        break;
                    }

                case 0xA7:
                    {
                        And(regA);
                        break;
                    }

                case 0xA8:
                    {
                        Xor(regB);
                        break;
                    }

                case 0xA9:
                    {
                        Xor(regC);
                        break;
                    }

                case 0xAA:
                    {
                        Xor(regD);
                        break;
                    }

                case 0xAB:
                    {
                        Xor(regE);
                        break;
                    }

                case 0xAC:
                    {
                        Xor(regH);
                        break;
                    }

                case 0xAD:
                    {
                        Xor(regL);
                        break;
                    }

                case 0xAE:
                    {
                        Xor(MemIoImpl.Peek8(GetRegHL()));
                        break;
                    }

                case 0xAF:
                    {
                        Xor(regA);
                        break;
                    }

                case 0xB0:
                    {
                        Or(regB);
                        break;
                    }

                case 0xB1:
                    {
                        Or(regC);
                        break;
                    }

                case 0xB2:
                    {
                        Or(regD);
                        break;
                    }

                case 0xB3:
                    {
                        Or(regE);
                        break;
                    }

                case 0xB4:
                    {
                        Or(regH);
                        break;
                    }

                case 0xB5:
                    {
                        Or(regL);
                        break;
                    }

                case 0xB6:
                    {
                        Or(MemIoImpl.Peek8(GetRegHL()));
                        break;
                    }

                case 0xB7:
                    {
                        Or(regA);
                        break;
                    }

                case 0xB8:
                    {
                        Cp(regB);
                        break;
                    }

                case 0xB9:
                    {
                        Cp(regC);
                        break;
                    }

                case 0xBA:
                    {
                        Cp(regD);
                        break;
                    }

                case 0xBB:
                    {
                        Cp(regE);
                        break;
                    }

                case 0xBC:
                    {
                        Cp(regH);
                        break;
                    }

                case 0xBD:
                    {
                        Cp(regL);
                        break;
                    }

                case 0xBE:
                    {
                        Cp(MemIoImpl.Peek8(GetRegHL()));
                        break;
                    }

                case 0xBF:
                    {
                        Cp(regA);
                        break;
                    }

                case 0xC0:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        if ((sz5h3pnFlags & ZERO_MASK) == 0)
                        {
                            regPC = memptr = Pop();
                        }

                        break;
                    }

                case 0xC1:
                    {
                        SetRegBC(Pop());
                        break;
                    }

                case 0xC2:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        if ((sz5h3pnFlags & ZERO_MASK) == 0)
                        {
                            regPC = memptr;
                            break;
                        }

                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0xC3:
                    {
                        memptr = regPC = MemIoImpl.Peek16(regPC);
                        break;
                    }

                case 0xC4:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        if ((sz5h3pnFlags & ZERO_MASK) == 0)
                        {
                            MemIoImpl.AddressOnBus((regPC + 1) & 0xffff, 1);
                            Push(regPC + 2);
                            regPC = memptr;
                            break;
                        }

                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0xC5:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        Push(GetRegBC());
                        break;
                    }

                case 0xC6:
                    {
                        Add(MemIoImpl.Peek8(regPC));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xC7:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        Push(regPC);
                        regPC = memptr = 0x00;
                        break;
                    }

                case 0xC8:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        if ((sz5h3pnFlags & ZERO_MASK) != 0)
                        {
                            regPC = memptr = Pop();
                        }

                        break;
                    }

                case 0xC9:
                    {
                        regPC = memptr = Pop();
                        break;
                    }

                case 0xCA:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        if ((sz5h3pnFlags & ZERO_MASK) != 0)
                        {
                            regPC = memptr;
                            break;
                        }

                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0xCB:
                    {
                        DecodeCB();
                        break;
                    }

                case 0xCC:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        if ((sz5h3pnFlags & ZERO_MASK) != 0)
                        {
                            MemIoImpl.AddressOnBus((regPC + 1) & 0xffff, 1);
                            Push(regPC + 2);
                            regPC = memptr;
                            break;
                        }

                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0xCD:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        MemIoImpl.AddressOnBus((regPC + 1) & 0xffff, 1);
                        Push(regPC + 2);
                        regPC = memptr;
                        break;
                    }

                case 0xCE:
                    {
                        Adc(MemIoImpl.Peek8(regPC));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xCF:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        Push(regPC);
                        regPC = memptr = 0x08;
                        break;
                    }

                case 0xD0:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        if (!carryFlag)
                        {
                            regPC = memptr = Pop();
                        }

                        break;
                    }

                case 0xD1:
                    {
                        SetRegDE(Pop());
                        break;
                    }

                case 0xD2:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        if (!carryFlag)
                        {
                            regPC = memptr;
                            break;
                        }

                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0xD3:
                    {
                        int work8 = MemIoImpl.Peek8(regPC);
                        memptr = regA << 8;
                        MemIoImpl.OutPort(memptr | work8, regA);
                        memptr |= ((work8 + 1) & 0xff);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xD4:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        if (!carryFlag)
                        {
                            MemIoImpl.AddressOnBus((regPC + 1) & 0xffff, 1);
                            Push(regPC + 2);
                            regPC = memptr;
                            break;
                        }

                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0xD5:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        Push(GetRegDE());
                        break;
                    }

                case 0xD6:
                    {
                        Sub(MemIoImpl.Peek8(regPC));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xD7:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        Push(regPC);
                        regPC = memptr = 0x10;
                        break;
                    }

                case 0xD8:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        if (carryFlag)
                        {
                            regPC = memptr = Pop();
                        }

                        break;
                    }

                case 0xD9:
                    {
                        int work8 = regB;
                        regB = regBx;
                        regBx = work8;
                        work8 = regC;
                        regC = regCx;
                        regCx = work8;
                        work8 = regD;
                        regD = regDx;
                        regDx = work8;
                        work8 = regE;
                        regE = regEx;
                        regEx = work8;
                        work8 = regH;
                        regH = regHx;
                        regHx = work8;
                        work8 = regL;
                        regL = regLx;
                        regLx = work8;
                        break;
                    }

                case 0xDA:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        if (carryFlag)
                        {
                            regPC = memptr;
                            break;
                        }

                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0xDB:
                    {
                        memptr = (regA << 8) | MemIoImpl.Peek8(regPC);
                        regA = MemIoImpl.InPort(memptr++);
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xDC:
                    {
                        memptr = MemIoImpl.Peek16(regPC);
                        if (carryFlag)
                        {
                            MemIoImpl.AddressOnBus((regPC + 1) & 0xffff, 1);
                            Push(regPC + 2);
                            regPC = memptr;
                            break;
                        }

                        regPC = (regPC + 2) & 0xffff;
                        break;
                    }

                case 0xDD:
                    {
                        opCode = MemIoImpl.FetchOpcode(regPC);
                        regPC = (regPC + 1) & 0xffff;
                        regR++;
                        regIX = DecodeDDFD(opCode, regIX);
                        break;
                    }

                case 0xDE:
                    {
                        Sbc(MemIoImpl.Peek8(regPC));
                        regPC = (regPC + 1) & 0xffff;
                        break;
                    }

                case 0xDF:
                    {
                        MemIoImpl.AddressOnBus(GetPairIR(), 1);
                        Push(regPC);
                        regPC = memptr = 0x18;
                        break;
                    }

                case 0xE0:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    if ((sz5h3pnFlags & PARITY_MASK) == 0)
                    {
                        regPC = memptr = Pop();
                    }

                    break;

                case 0xE1:
                    SetRegHL(Pop());
                    break;

                case 0xE2:
                    memptr = MemIoImpl.Peek16(regPC);
                    if ((sz5h3pnFlags & PARITY_MASK) == 0)
                    {
                        regPC = memptr;
                        break;
                    }

                    regPC = (regPC + 2) & 0xffff;
                    break;

                case 0xE3:
                    {
                        int work16 = regH;
                        int work8 = regL;
                        SetRegHL(MemIoImpl.Peek16(regSP));
                        MemIoImpl.AddressOnBus((regSP + 1) & 0xffff, 1);
                        MemIoImpl.Poke8((regSP + 1) & 0xffff, work16);
                        MemIoImpl.Poke8(regSP, work8);
                        MemIoImpl.AddressOnBus(regSP, 2);
                        memptr = GetRegHL();
                        break;
                    }

                case 0xE4:
                    memptr = MemIoImpl.Peek16(regPC);
                    if ((sz5h3pnFlags & PARITY_MASK) == 0)
                    {
                        MemIoImpl.AddressOnBus((regPC + 1) & 0xffff, 1);
                        Push(regPC + 2);
                        regPC = memptr;
                        break;
                    }

                    regPC = (regPC + 2) & 0xffff;
                    break;

                case 0xE5:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    Push(GetRegHL());
                    break;

                case 0xE6:
                    And(MemIoImpl.Peek8(regPC));
                    regPC = (regPC + 1) & 0xffff;
                    break;

                case 0xE7:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    Push(regPC);
                    regPC = memptr = 0x20;
                    break;

                case 0xE8:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    if ((sz5h3pnFlags & PARITY_MASK) != 0)
                    {
                        regPC = memptr = Pop();
                    }

                    break;

                case 0xE9:
                    regPC = GetRegHL();
                    break;

                case 0xEA:
                    memptr = MemIoImpl.Peek16(regPC);
                    if ((sz5h3pnFlags & PARITY_MASK) != 0)
                    {
                        regPC = memptr;
                        break;
                    }

                    regPC = (regPC + 2) & 0xffff;
                    break;

                case 0xEB:
                    {
                        int work8 = regH;
                        regH = regD;
                        regD = work8;
                        work8 = regL;
                        regL = regE;
                        regE = work8;
                        break;
                    }

                case 0xEC:
                    memptr = MemIoImpl.Peek16(regPC);
                    if ((sz5h3pnFlags & PARITY_MASK) != 0)
                    {
                        MemIoImpl.AddressOnBus((regPC + 1) & 0xffff, 1);
                        Push(regPC + 2);
                        regPC = memptr;
                        break;
                    }

                    regPC = (regPC + 2) & 0xffff;
                    break;

                case 0xED:
                    opCode = MemIoImpl.FetchOpcode(regPC);
                    regPC = (regPC + 1) & 0xffff;
                    regR++;
                    DecodeED(opCode);
                    break;

                case 0xEE:
                    Xor(MemIoImpl.Peek8(regPC));
                    regPC = (regPC + 1) & 0xffff;
                    break;

                case 0xEF:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    Push(regPC);
                    regPC = memptr = 0x28;
                    break;

                case 0xF0:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    if (sz5h3pnFlags < SIGN_MASK)
                    {
                        regPC = memptr = Pop();
                    }

                    break;

                case 0xF1:
                    SetRegAF(Pop());
                    break;

                case 0xF2:
                    memptr = MemIoImpl.Peek16(regPC);
                    if (sz5h3pnFlags < SIGN_MASK)
                    {
                        regPC = memptr;
                        break;
                    }

                    regPC = (regPC + 2) & 0xffff;
                    break;

                case 0xF3:
                    ffIFF1 = ffIFF2 = false;
                    break;

                case 0xF4:
                    memptr = MemIoImpl.Peek16(regPC);
                    if (sz5h3pnFlags < SIGN_MASK)
                    {
                        MemIoImpl.AddressOnBus((regPC + 1) & 0xffff, 1);
                        Push(regPC + 2);
                        regPC = memptr;
                        break;
                    }

                    regPC = (regPC + 2) & 0xffff;
                    break;

                case 0xF5:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    Push(GetRegAF());
                    break;

                case 0xF6:
                    Or(MemIoImpl.Peek8(regPC));
                    regPC = (regPC + 1) & 0xffff;
                    break;

                case 0xF7:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    Push(regPC);
                    regPC = memptr = 0x30;
                    break;

                case 0xF8:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    if (sz5h3pnFlags > 0x7f)
                    {
                        regPC = memptr = Pop();
                    }

                    break;

                case 0xF9:
                    MemIoImpl.AddressOnBus(GetPairIR(), 2);
                    regSP = GetRegHL();
                    break;

                case 0xFA:
                    memptr = MemIoImpl.Peek16(regPC);
                    if (sz5h3pnFlags > 0x7f)
                    {
                        regPC = memptr;
                        break;
                    }

                    regPC = (regPC + 2) & 0xffff;
                    break;

                case 0xFB:
                    ffIFF1 = ffIFF2 = true;
                    pendingEI = true;
                    break;

                case 0xFC:
                    memptr = MemIoImpl.Peek16(regPC);
                    if (sz5h3pnFlags > 0x7f)
                    {
                        MemIoImpl.AddressOnBus((regPC + 1) & 0xffff, 1);
                        Push(regPC + 2);
                        regPC = memptr;
                        break;
                    }

                    regPC = (regPC + 2) & 0xffff;
                    break;

                case 0xFD:
                    opCode = MemIoImpl.FetchOpcode(regPC);
                    regPC = (regPC + 1) & 0xffff;
                    regR++;
                    regIY = DecodeDDFD(opCode, regIY);
                    break;

                case 0xFE:
                    Cp(MemIoImpl.Peek8(regPC));
                    regPC = (regPC + 1) & 0xffff;
                    break;

                case 0xFF:
                    MemIoImpl.AddressOnBus(GetPairIR(), 1);
                    Push(regPC);
                    regPC = memptr = 0x38;
                    break;
            }
        }

        private void DecRegBC()
        {
            if (--regC >= 0)
            {
                return;
            }

            regC = 0xff;
            if (--regB >= 0)
            {
                return;
            }

            regB = 0xff;
        }

        private void DecRegDE()
        {
            if (--regE >= 0)
            {
                return;
            }

            regE = 0xff;
            if (--regD >= 0)
            {
                return;
            }

            regD = 0xff;
        }

        private void DecRegHL()
        {
            if (--regL >= 0)
            {
                return;
            }

            regL = 0xff;
            if (--regH >= 0)
            {
                return;
            }

            regH = 0xff;
        }

        private int Inc8(int oper8)
        {
            oper8 = (oper8 + 1) & 0xff;
            sz5h3pnFlags = sz53n_addTable[oper8];
            if ((oper8 & 0x0f) == 0)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            if (oper8 == 0x80)
            {
                sz5h3pnFlags |= OVERFLOW_MASK;
            }

            flagQ = true;
            return oper8;
        }

        private void IncRegBC()
        {
            if (++regC < 0x100)
            {
                return;
            }

            regC = 0;
            if (++regB < 0x100)
            {
                return;
            }

            regB = 0;
        }

        private void IncRegDE()
        {
            if (++regE < 0x100)
            {
                return;
            }

            regE = 0;
            if (++regD < 0x100)
            {
                return;
            }

            regD = 0;
        }

        private void IncRegHL()
        {
            if (++regL < 0x100)
            {
                return;
            }

            regL = 0;
            if (++regH < 0x100)
            {
                return;
            }

            regH = 0;
        }

        private void Ind()
        {
            memptr = GetRegBC();
            MemIoImpl.AddressOnBus(GetPairIR(), 1);
            int work8 = MemIoImpl.InPort(memptr);
            MemIoImpl.Poke8(GetRegHL(), work8);
            memptr--;
            regB = (regB - 1) & 0xff;
            DecRegHL();
            sz5h3pnFlags = sz53pn_addTable[regB];
            if (work8 > 0x7f)
            {
                sz5h3pnFlags |= ADDSUB_MASK;
            }

            carryFlag = false;
            int tmp = work8 + ((regC - 1) & 0xff);
            if (tmp > 0xff)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
                carryFlag = true;
            }

            if ((sz53pn_addTable[((tmp & 0x07) ^ regB)] & PARITY_MASK) == PARITY_MASK)
            {
                sz5h3pnFlags |= PARITY_MASK;
            }
            else
            {
                sz5h3pnFlags &= ~PARITY_MASK;
            }

            flagQ = true;
        }

        private void Ini()
        {
            memptr = GetRegBC();
            MemIoImpl.AddressOnBus(GetPairIR(), 1);
            int work8 = MemIoImpl.InPort(memptr);
            MemIoImpl.Poke8(GetRegHL(), work8);
            memptr++;
            regB = (regB - 1) & 0xff;
            IncRegHL();
            sz5h3pnFlags = sz53pn_addTable[regB];
            if (work8 > 0x7f)
            {
                sz5h3pnFlags |= ADDSUB_MASK;
            }

            carryFlag = false;
            int tmp = work8 + ((regC + 1) & 0xff);
            if (tmp > 0xff)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
                carryFlag = true;
            }

            if ((sz53pn_addTable[((tmp & 0x07) ^ regB)] & PARITY_MASK) == PARITY_MASK)
            {
                sz5h3pnFlags |= PARITY_MASK;
            }
            else
            {
                sz5h3pnFlags &= ~PARITY_MASK;
            }

            flagQ = true;
        }

        private void Interruption()
        {
            lastFlagQ = false;
            if (halted)
            {
                halted = false;
                regPC = (regPC + 1) & 0xffff;
            }

            MemIoImpl.InterruptHandlingTime(7);
            regR++;
            ffIFF1 = ffIFF2 = false;
            Push(regPC);
            if (modeINT == IntMode.IM2)
            {
                regPC = MemIoImpl.Peek16((regI << 8) | 0xff);
            }
            else
            {
                regPC = 0x0038;
            }

            memptr = regPC;
        }

        private void Ldd()
        {
            int work8 = MemIoImpl.Peek8(GetRegHL());
            int regDE = GetRegDE();
            MemIoImpl.Poke8(regDE, work8);
            MemIoImpl.AddressOnBus(regDE, 2);
            DecRegHL();
            DecRegDE();
            DecRegBC();
            work8 += regA;
            sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZ_MASK) | (work8 & BIT3_MASK);
            if ((work8 & ADDSUB_MASK) != 0)
            {
                sz5h3pnFlags |= BIT5_MASK;
            }

            if (regC != 0 || regB != 0)
            {
                sz5h3pnFlags |= PARITY_MASK;
            }

            flagQ = true;
        }

        private void Ldi()
        {
            int work8 = MemIoImpl.Peek8(GetRegHL());
            int regDE = GetRegDE();
            MemIoImpl.Poke8(regDE, work8);
            MemIoImpl.AddressOnBus(regDE, 2);
            IncRegHL();
            IncRegDE();
            DecRegBC();
            work8 += regA;
            sz5h3pnFlags = (sz5h3pnFlags & FLAG_SZ_MASK) | (work8 & BIT3_MASK);
            if ((work8 & ADDSUB_MASK) != 0)
            {
                sz5h3pnFlags |= BIT5_MASK;
            }

            if (regC != 0 || regB != 0)
            {
                sz5h3pnFlags |= PARITY_MASK;
            }

            flagQ = true;
        }

        private void Nmi()
        {
            lastFlagQ = false;
            MemIoImpl.FetchOpcode(regPC);
            MemIoImpl.InterruptHandlingTime(1);
            if (halted)
            {
                halted = false;
                regPC = (regPC + 1) & 0xffff;
            }

            regR++;
            ffIFF1 = false;
            Push(regPC);
            regPC = memptr = 0x0066;
        }

        private void Or(int oper8)
        {
            regA = (regA | oper8) & 0xff;
            carryFlag = false;
            sz5h3pnFlags = sz53pn_addTable[regA];
            flagQ = true;
        }

        private void Outd()
        {
            MemIoImpl.AddressOnBus(GetPairIR(), 1);
            regB = (regB - 1) & 0xff;
            memptr = GetRegBC();
            int work8 = MemIoImpl.Peek8(GetRegHL());
            MemIoImpl.OutPort(memptr, work8);
            memptr--;
            DecRegHL();
            carryFlag = false;
            if (work8 > 0x7f)
            {
                sz5h3pnFlags = sz53n_subTable[regB];
            }
            else
            {
                sz5h3pnFlags = sz53n_addTable[regB];
            }

            if ((regL + work8) > 0xff)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
                carryFlag = true;
            }

            if ((sz53pn_addTable[(((regL + work8) & 0x07) ^ regB)] & PARITY_MASK) == PARITY_MASK)
            {
                sz5h3pnFlags |= PARITY_MASK;
            }

            flagQ = true;
        }

        private void Outi()
        {
            MemIoImpl.AddressOnBus(GetPairIR(), 1);
            regB = (regB - 1) & 0xff;
            memptr = GetRegBC();
            int work8 = MemIoImpl.Peek8(GetRegHL());
            MemIoImpl.OutPort(memptr, work8);
            memptr++;
            IncRegHL();
            carryFlag = false;
            if (work8 > 0x7f)
            {
                sz5h3pnFlags = sz53n_subTable[regB];
            }
            else
            {
                sz5h3pnFlags = sz53n_addTable[regB];
            }

            if ((regL + work8) > 0xff)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
                carryFlag = true;
            }

            if ((sz53pn_addTable[(((regL + work8) & 0x07) ^ regB)] & PARITY_MASK) == PARITY_MASK)
            {
                sz5h3pnFlags |= PARITY_MASK;
            }

            flagQ = true;
        }

        private int Pop()
        {
            int word = MemIoImpl.Peek16(regSP);
            regSP = (regSP + 2) & 0xffff;
            return word;
        }

        private void Push(int word)
        {
            regSP = (regSP - 1) & 0xffff;
            MemIoImpl.Poke8(regSP, word >> 8);
            regSP = (regSP - 1) & 0xffff;
            MemIoImpl.Poke8(regSP, word);
        }

        private int Rl(int oper8)
        {
            bool carry = carryFlag;
            carryFlag = (oper8 > 0x7f);
            oper8 = (oper8 << 1) & 0xfe;
            if (carry)
            {
                oper8 |= CARRY_MASK;
            }

            sz5h3pnFlags = sz53pn_addTable[oper8];
            flagQ = true;
            return oper8;
        }

        private int Rlc(int oper8)
        {
            carryFlag = (oper8 > 0x7f);
            oper8 = (oper8 << 1) & 0xfe;
            if (carryFlag)
            {
                oper8 |= CARRY_MASK;
            }

            sz5h3pnFlags = sz53pn_addTable[oper8];
            flagQ = true;
            return oper8;
        }

        private void Rld()
        {
            int aux = regA & 0x0f;
            memptr = GetRegHL();
            int memHL = MemIoImpl.Peek8(memptr);
            regA = (regA & 0xf0) | (memHL >> 4);
            MemIoImpl.AddressOnBus(memptr, 4);
            MemIoImpl.Poke8(memptr, ((memHL << 4) | aux) & 0xff);
            sz5h3pnFlags = sz53pn_addTable[regA];
            memptr++;
            flagQ = true;
        }

        private int Rr(int oper8)
        {
            bool carry = carryFlag;
            carryFlag = (oper8 & CARRY_MASK) != 0;
            oper8 >>= 1;
            if (carry)
            {
                oper8 |= SIGN_MASK;
            }

            sz5h3pnFlags = sz53pn_addTable[oper8];
            flagQ = true;
            return oper8;
        }

        private int Rrc(int oper8)
        {
            carryFlag = (oper8 & CARRY_MASK) != 0;
            oper8 >>= 1;
            if (carryFlag)
            {
                oper8 |= SIGN_MASK;
            }

            sz5h3pnFlags = sz53pn_addTable[oper8];
            flagQ = true;
            return oper8;
        }

        private void Rrd()
        {
            int aux = (regA & 0x0f) << 4;
            memptr = GetRegHL();
            int memHL = MemIoImpl.Peek8(memptr);
            regA = (regA & 0xf0) | (memHL & 0x0f);
            MemIoImpl.AddressOnBus(memptr, 4);
            MemIoImpl.Poke8(memptr, (memHL >> 4) | aux);
            sz5h3pnFlags = sz53pn_addTable[regA];
            memptr++;
            flagQ = true;
        }

        private void Sbc(int oper8)
        {
            int res = regA - oper8;
            if (carryFlag)
            {
                res--;
            }

            carryFlag = res < 0;
            res &= 0xff;
            sz5h3pnFlags = sz53n_subTable[res];
            if (((regA ^ oper8 ^ res) & 0x10) != 0)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            if (((regA ^ oper8) & (regA ^ res)) > 0x7f)
            {
                sz5h3pnFlags |= OVERFLOW_MASK;
            }

            regA = res;
            flagQ = true;
        }

        private void Sbc16(int reg16)
        {
            int regHL = GetRegHL();
            memptr = regHL + 1;
            int res = regHL - reg16;
            if (carryFlag)
            {
                res--;
            }

            carryFlag = res < 0;
            res &= 0xffff;
            SetRegHL(res);
            sz5h3pnFlags = sz53n_subTable[regH];
            if (res != 0)
            {
                sz5h3pnFlags &= ~ZERO_MASK;
            }

            if (((res ^ regHL ^ reg16) & 0x1000) != 0)
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            if (((regHL ^ reg16) & (regHL ^ res)) > 0x7fff)
            {
                sz5h3pnFlags |= OVERFLOW_MASK;
            }

            flagQ = true;
        }

        private int Sla(int oper8)
        {
            carryFlag = (oper8 > 0x7f);
            oper8 = (oper8 << 1) & 0xfe;
            sz5h3pnFlags = sz53pn_addTable[oper8];
            flagQ = true;
            return oper8;
        }

        private int Sll(int oper8)
        {
            carryFlag = (oper8 > 0x7f);
            oper8 = ((oper8 << 1) | CARRY_MASK) & 0xff;
            sz5h3pnFlags = sz53pn_addTable[oper8];
            flagQ = true;
            return oper8;
        }

        private int Sra(int oper8)
        {
            int sign = oper8 & SIGN_MASK;
            carryFlag = (oper8 & CARRY_MASK) != 0;
            oper8 = (oper8 >> 1) | sign;
            sz5h3pnFlags = sz53pn_addTable[oper8];
            flagQ = true;
            return oper8;
        }

        private int Srl(int oper8)
        {
            carryFlag = (oper8 & CARRY_MASK) != 0;
            oper8 >>= 1;
            sz5h3pnFlags = sz53pn_addTable[oper8];
            flagQ = true;
            return oper8;
        }

        private void Sub(int oper8)
        {
            int res = regA - oper8;
            carryFlag = res < 0;
            res &= 0xff;
            sz5h3pnFlags = sz53n_subTable[res];
            if ((res & 0x0f) > (regA & 0x0f))
            {
                sz5h3pnFlags |= HALFCARRY_MASK;
            }

            if (((regA ^ oper8) & (regA ^ res)) > 0x7f)
            {
                sz5h3pnFlags |= OVERFLOW_MASK;
            }

            regA = res;
            flagQ = true;
        }

        private void Xor(int oper8)
        {
            regA = (regA ^ oper8) & 0xff;
            carryFlag = false;
            sz5h3pnFlags = sz53pn_addTable[regA];
            flagQ = true;
        }
    }
}