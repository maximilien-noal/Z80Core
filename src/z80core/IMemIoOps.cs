namespace Z80core
{
    public interface IMemIoOps
    {
        int FetchOpcode(int address);

        int Peek8(int address);

        void Poke8(int address, int value);

        int Peek16(int address)
        {
            int lsb = Peek8(address);
            int msb = Peek8(address + 1);
            return (msb << 8) | lsb;
        }

        void Poke16(int address, int word)
        {
            Poke8(address, word);
            Poke8(address + 1, ((int)((uint)word >> 8)));
        }

        int InPort(int port);

        void OutPort(int port, int value);

        void AddressOnBus(int address, int tstates);

        void InterruptHandlingTime(int tstates);

        bool IsActiveINT();

        bool SetActiveINT(bool val);

        long GetTstates();

        void Reset();
    }
}