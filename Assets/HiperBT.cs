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

        writer.WriteStr("set,/par/dev/ntrip/a/imode,cmd\n\r");
        writer.WriteStr("print,/par/dev/ntrip/a/imode\n\r");
        writer.WriteStr("list,/dev\n\r");
        writer.WriteStr("em,/cur/term,/msg/nmea/GGA:.05\n\r");
        writer.WriteStr("set,/par/cur/term/imode,rtcm3\n\r");
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
