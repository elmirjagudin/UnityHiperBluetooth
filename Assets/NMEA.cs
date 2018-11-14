using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class NMEA : MonoBehaviour
{

	AndroidJavaClass hiper;
    NTRipClient ntrip;

	void Start ()
    {
#if !UNITY_EDITOR
        hiper = new AndroidJavaClass("ws.brab.pl.HiperReader");
        Debug.LogFormat("Xhyper '{0}'", hiper);
        hiper.CallStatic("Init");
#endif

        ntrip = new NTRipClient("example.com", 8500, "RTCM3_GNSS", "username", "password", PushNtrip);
	}

	void Update ()
    {
#if !UNITY_EDITOR
        var line = hiper.CallStatic<string>("GetNMEA");
        gameObject.GetComponent<Text>().text = line;
        Debug.LogFormat("hiperLine: '{0}'", line);
        ntrip.UpdateRoverPosition(line);
//        ntrip.UpdateRoverPosition(55, 14, 60);
#endif
	}

    void foo()
    {
        byte[] data = new byte[]{
              4, 10, 11,   2,
              0, 0,   0, 100,
            200, 1}
            ;
        PushNtrip(data, 8);
    }

    void PushNtrip(byte[] data, int len)
    {
        Debug.LogFormat("pushing {0} bytes", len);
        string str = "pushedR  : ";

        for (int i = 0; i < len; i += 1)
        {
            str += string.Format("{0:x} ", data[i]);
        }

#if !UNITY_EDITOR
        AndroidJNI.AttachCurrentThread(); // something something, threads, android, dalwik, GC

        hiper.CallStatic("PushRTCM", data, len);
        Debug.Log(str);

        AndroidJNI.DetachCurrentThread();
#endif

    }

    void OnApplicationQuit()
    {
        ntrip.Abort();
    }
}
