/*
 * NTRipclient.
 *
 * Client for retrieving correction data and pushing correction data
 * to the GPS via comport.
 *
 * NTRipClient ntclient = new NTRipClient(address, portNumber, mountPoint, user, pass, comportName);
 * ...
 * ntclient.UpdateRoverPosition(ggaStr);
 * if (ntclient.status != 0) {
 *     ...
 * }
 */
using System;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using UnityEngine;

class NTRipClient
{
    public delegate void RTCMDataSink(byte[] data, int len);

    enum Responses
    {
        ICY_200_OK,
        HTTP_401_Unauthorized,
        SOURCETABLE_200,
        Unexpected,
    }

    /// <summary>
    /// Timeout for how long for waiting for thread to stop.
    /// </summary>
    const int THREAD_STOP_TIMEOUT = 1000;
    const int DATA_CHECK_DELAY = 500;

    /// <summary>
    /// Correction site setup
    /// </summary>
    string username;
    string password;
    string addr;
    string mount;
    int port;
    string uri;
    TcpClient client;
    RTCMDataSink dataSink;

    /// <summary>
    /// Latest gga position from GPS.
    /// </summary>
    string latestGGA;

    /// <summary>
    /// Stats.
    /// </summary>
    int total_read;

    /// <summary>
    /// Thread for reading correction data and send to comport.
    /// </summary>
    Thread responseThread;
    volatile bool threadRunning = false;

    int _status;
    /// <summary>
    /// Status of the ntrip client. 0 if everything is ok.
    /// </summary>
    public int status
    {
        get { return _status; }
    }

    string _err;
    /// <summary>
    /// Error string and cause why status != 0
    /// </summary>
    public string err
    {
        get { return _err; }
    }

    /// <summary>
    /// Creates a NTRip client.
    ///
    /// The NTRip client owns the serial port and caller does not need to care for it. This is useful if we have two comports, one for reading and sending commands, and one for sending correction data.
    /// </summary>
    /// <param name="address">Address of the correction data web site.</param>
    /// <param name="portNumber">Port number of the correction data web site.</param>
    /// <param name="mountPoint">Mounting point of the correction data web site. E.g in www.sonesite.com/rtcm32/ rtcn32 is the mountpoint.</param>
    /// <param name="user">User name.</param>
    /// <param name="pass">Password to the correction web site.</param>
    public NTRipClient(string address, int portNumber, string mountPoint, string user, string pass, RTCMDataSink dataSink)
    {
        username = user;
        password = pass;
        addr = address;
        port = portNumber;
        mount = mountPoint;
        this.dataSink = dataSink;
        client = null;

        uri = "http://" + addr + ":" + port + "/" + mount;

        total_read = 0;
    }

    /// <summary>
    /// Stop all threads and release resources.
    /// </summary>
    public void Abort()
    {
        ResponseThreadStop();
        if (client != null)
        {
            client.Close();
            client = null;
        }
    }

    /// <summary>
    /// Sends GGA data from GPS to correction site.
    /// </summary>
    private void SendGGA()
    {
        byte[] toBytes = Encoding.ASCII.GetBytes(latestGGA + "\r\n");

        if (client == null)
        {
            return;
        }
        client.GetStream().Write(toBytes, 0, toBytes.Length);
    }

    /// <summary>
    /// Start thread for reading correction data.
    /// </summary>
    private void ResponseThreadStart()
    {
        if (responseThread != null)
        {
            if (responseThread.IsAlive)
            {
                /* Thread is running and alive, just return. */
                return;
            }
            ResponseThreadStop();
        }

        /* Thread not running, or stopped. */
        threadRunning = true;
        responseThread = new Thread(new ThreadStart(ResponseThreadLoop));
        responseThread.Start();
    }

    /// <summary>
    /// Stop the thread for data.
    /// </summary>
    private void ResponseThreadStop()
    {
        if (responseThread != null)
        {
            threadRunning = false;
            if (!responseThread.Join(THREAD_STOP_TIMEOUT))
            {
                /* Thread failed to terminate, last resort Abort it. */
                responseThread.Abort();
            }
            responseThread = null;
        }
    }

    Responses ParseReply(byte[] reply, int replyLength)
    {
        var content = Encoding.UTF8.GetString(reply, 0, replyLength);

        if (Regex.IsMatch(content, "^ICY 200.*", RegexOptions.IgnoreCase))
        {
            return Responses.ICY_200_OK;
        }

        if (Regex.IsMatch(content, @"^HTTP/1\.\d 401.*", RegexOptions.IgnoreCase))
        {
            return Responses.HTTP_401_Unauthorized;
        }

        if (Regex.IsMatch(content, "^SOURCETABLE 200.*", RegexOptions.IgnoreCase))
        {
            return Responses.SOURCETABLE_200;
        }

        return Responses.Unexpected;

    }

    /// <summary>
    /// Thread for reading correction data.
    /// </summary>
    private void ResponseThreadLoop()
    {
        byte[] buf = new byte[1024];
        int len;
        int to_read;
        int count = 0;

        /*
         * Get Status code, Hopefully "ICY 200 OK"
         */
        try
        {
            while (threadRunning)
            {
                if (client.Available > 0)
                {
                    to_read = Math.Min(client.Available, 1024);
                    if ((len = client.GetStream().Read(buf, 0, to_read)) > 0)
                    {
                        switch (ParseReply(buf, len))
                        {
                            case Responses.HTTP_401_Unauthorized:
                                Debug.Log("authorization error");
                                _err = "authorization error";
                                _status = -3;
                                return;
                            case Responses.Unexpected:
                                Debug.Log("unexpected reply from host");
                                _err = "unexpected reply from host";
                                _status = -4;
                                return;
                            case Responses.SOURCETABLE_200:
                                Debug.Log("invalid mounting point");
                                _err = "invalid mounting point";
                                _status = -4;
                                return;
                            case Responses.ICY_200_OK:
                                /* success, start reading RTCM data */
                                break;
                        }
                        total_read += len;
                        break;
                    }
                    Thread.Sleep(DATA_CHECK_DELAY);
                }
            }
        }
        catch (Exception e)
        {
            _err = "Failed to get response: " + e.Message;
            _status = -1;
            Debug.LogFormat("NTRipClient: Error: ResponseThreadLoop: Read from BaseStation server {0}", e.Message);
            return;
        }

        try
        {
            while (threadRunning && _status == 0)
            {
                if (count <= 0)
                {
                    /* Send GGA to correction site. */
                    SendGGA();
                    count = 30;
                }
                count--;
                while (client.Available > 0)
                {
                    to_read = Math.Min(client.Available, 1024);
                    if ((len = client.GetStream().Read(buf, 0, to_read)) > 0)
                    {
                        total_read += len;
                        SerialPortWrite(buf, len);
                    }
                }

                Thread.Sleep(DATA_CHECK_DELAY);
            }
        }
        catch (Exception e)
        {
            _status = -1;
            _err = "Response loop failed: " + e.Message;
            Debug.LogFormat("NTRipClient: Error: ResponseThreadLoop: {0}", e.Message);
        }
    }

    private void SerialPortWrite(byte[] buf, int len)
    {
        Debug.LogFormat("got {0} bytes  data", len);
        dataSink(buf, len);
    }

    private void SetupWebRequest()
    {
        string msg;
        string auth;

        if (client != null)
        {
            return;
        }

        Debug.Log("setting up client");

        auth = username + ":" + password;
        auth = Convert.ToBase64String(Encoding.ASCII.GetBytes(auth));
        msg = string.Format("GET /{0} HTTP/1.0\r\nUser-Agent: NTRIP Brab\r\nAccept: */*\r\nConnection: close\r\nAuthorization: Basic {1}\r\n\r\n", mount, auth);
        try
        {
            client = new TcpClient();
            client.Connect(addr, port);
            byte[] toSend = Encoding.Default.GetBytes(msg);
            client.GetStream().Write(toSend, 0, toSend.Length);
        }
        catch (SocketException e)
        {
            _status = -2;
            _err = "Failed to setup GGA request. " + e.Message;
            Debug.Log("NTRipClient: Error: SetupWebRequest: " + uri + " Error: " + _err);
        }
        catch (Exception e)
        {
            _status = -1;
            _err = "Failed to setup GGA request. " + e.Message;
            Debug.Log("NTRipClient: Error: SetupWebRequest: " + uri + " Error: " + _err);
        }
    }

    /// <summary>
    /// Updates the rover's position.
    ///
    /// Sends a request to correction data site to send new correction data.
    /// The function will send a async request, and will send result rtcm data to
    /// the comport when the correction request is complete.
    /// </summary>
    /// <param name="pos">The rover's position.</param>
    public void UpdateRoverPosition(string pos)
    {
        latestGGA = pos;
        if (_status != 0)
        {
            return;
        }

        SetupWebRequest();
        if (_status != 0)
        {
            return;
        }
        SendGGA();
        ResponseThreadStart();
    }

    byte Checksum(string str)
    {
        byte checksum = 0;

        foreach (var b in  Encoding.ASCII.GetBytes(str))
        {
            checksum ^= b;
        }
        return checksum;
    }

    public void UpdateRoverPosition(double latitude, double longitude, double altitude)
    {
        //$GPGGA,135205,5500.00,N,01400.00,E,4,10,1,200,M,1,M,8,0*67
        //var tst = "GPGGA,135205,5500.00,N,01400.00,E,4,10,1,200,M,1,M,8,0";

        //$GPGGA,140816,5500.00,N,01400.00,E,4,10,1,200,M,1,M,7,0*62"
        //var tst = "GPGGA,140816,5500.00,N,01400.00,E,4,10,1,200,M,1,M,7,0";

        var now = DateTime.UtcNow;
        var pos = string.Format("GPGGA,{0}{1}{2},5500.00,N,01400.00,E,4,10,1,200,M,1,M,7,0",
                                now.Hour, now.Minute, now.Second);

        var res = string.Format("${0}*{1:X}", pos, Checksum(pos));
        UpdateRoverPosition(res);
    }
}