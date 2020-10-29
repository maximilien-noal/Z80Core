using System;

namespace Z80core
{
    public class MemIoOps : IMemIoOps
    {
        private long tstates = 0;

        private byte[] z80Ports = new byte[0x10000];

        private byte[] z80Ram = new byte[0x10000];

        public MemIoOps(int ramSize, int portSize)
        {
            if (ramSize < 0 || ramSize > 0x10000)
                throw new IndexOutOfRangeException("ramSize Out of Range [0x0000 - 0x10000");
            if (ramSize > 0)
            {
                z80Ram = new byte[ramSize];
            }

            if (portSize < 0 || portSize > 0x10000)
                throw new IndexOutOfRangeException("portSize Out of Range [0x0000 - 0x10000");
            if (portSize > 0)
            {
                z80Ports = new byte[portSize];
            }
        }

        public virtual void AddressOnBus(int address, int tstates)
        {
            this.tstates += tstates;
        }

        public virtual int FetchOpcode(int address)
        {
            tstates += 4;
            return z80Ram[address] & 0xff;
        }

        public virtual long GetTstates()
        {
            return tstates;
        }

        public virtual int InPort(int port)
        {
            tstates += 4;
            return z80Ports[port] & 0xff;
        }

        public virtual void InterruptHandlingTime(int tstates)
        {
            this.tstates += tstates;
        }

        public virtual bool IsActiveINT()
        {
            return false;
        }

        public virtual void OutPort(int port, int value)
        {
            tstates += 4;
            z80Ports[port] = (byte)value;
        }

        public virtual int Peek16(int address)
        {
            int lsb = Peek8(address);
            int msb = Peek8(address + 1);
            return (msb << 8) | lsb;
        }

        public virtual int Peek8(int address)
        {
            tstates += 3;
            return z80Ram[address] & 0xff;
        }

        public virtual void Poke16(int address, int word)
        {
            Poke8(address, word);
            Poke8(address + 1, (int)((uint)word >> 8));
        }

        public virtual void Poke8(int address, int value)
        {
            tstates += 3;
            z80Ram[address] = (byte)value;
        }

        public virtual void Reset()
        {
            tstates = 0;
        }

        public bool SetActiveINT(bool val)
        {
            return false;
        }

        public virtual void SetPorts(byte[] ports)
        {
            z80Ram = ports;
        }

        public virtual void SetRam(byte[] ram)
        {
            z80Ram = ram;
        }
    }
}