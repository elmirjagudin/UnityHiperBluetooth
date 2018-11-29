using System;
using System.Text;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using brab.bluetooth;

public class HiperBT
{
    BluetoothSocket sock;
    StreamReader reader;
    HiperWriter writer;

    class HiperWriter : BinaryWriter
    {
        public HiperWriter(Stream stream) : base(stream) {}

        public void WriteStr(string str)
        {
            var bytes = Encoding.ASCII.GetBytes(str);
            Write(bytes, 0, bytes.Length);
        }
    }

    void ResetCmdMode(HiperWriter bw)
    {
        /*
         * it seems that by sending following data:
         *
         * 128 bytes of 0x02
         * 128 bytes of 0x01
         * bytes 0x0d 0x0a
         *
         * the Bluetooth terminal resets back from RCTM3 input mode to
         * command input mode
         */
        var magic = new byte[256];

        for (int i = 0; i < 128; i += 1)
        {
            magic[i] = 2;
            magic[128+i] = 1;
        }

        bw.Write(magic);
        bw.WriteStr("\r\n");
    }

    public HiperBT()
    {
        var ba = BluetoothAdapter.getDefaultAdapter();
        if (!ba.isEnabled())
        {
            throw new Exception("Bluetooth is disabled");
        }

        BluetoothDevice hiper = null;
        foreach (var c in ba.getBondedDevices())
        {
            if (c.getAddress() == "00:07:80:36:02:C6")
            {
                hiper = c;
            }
        }

        if (hiper == null)
        {
            throw new Exception("No hiper device found");
        }

        var uuid = UUID.fromString("00001101-0000-1000-8000-00805f9b34fb");
        sock = hiper.createRfcommSocketToServiceRecord(uuid);

        sock.connect();

        var istream = sock.getInputStream();
        var ostream = sock.getOutputStream();

        reader = new StreamReader(istream);
        writer = new HiperWriter(ostream);

        ResetCmdMode(writer);

        writer.WriteStr("set,/par/dev/ntrip/a/imode,cmd\r\n");
        writer.WriteStr("%ORANGE%list\r\n");
        writer.WriteStr("em,/cur/term,/msg/nmea/GGA:.05\r\n");
        //writer.WriteStr("dm,/cur/term,/msg/nmea/GGA\r\n");
        writer.WriteStr("set,/par/cur/term/imode,rtcm3\r\n");
        writer.Flush();
    }

    public string GetNMEA()
    {
        return reader.ReadLine();
    }

    public void PushRTCM(byte[] data, int len)
    {
        writer.Write(data, 0, len);
    }

    public void Disconnect()
    {
        sock.close();
    }
}
