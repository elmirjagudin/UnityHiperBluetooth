using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class NMEA : MonoBehaviour
{

	HiperBT hiper;
    NTRipClient ntrip;

	void Start ()
    {
#if !UNITY_EDITOR
        hiper = new HiperBT();
#endif

        ntrip = new NTRipClient("example.com", 8500, "RTCM3_GNSS", "username", "password", PushNtrip);
	}

	void Update ()
    {
#if !UNITY_EDITOR
        var line = hiper.GetNMEA();
        gameObject.GetComponent<Text>().text = line;
        Debug.LogFormat("hiperLine: '{0}'", line);
        if (line.StartsWith("$GPGGA"))
        {
            Debug.LogFormat("pushing line");
            ntrip.UpdateRoverPosition(line);
        }
        else
        {
            Debug.LogFormat("not an NMEA line");
        }
//        ntrip.UpdateRoverPosition(55, 14, 60);
#endif
	}


    void PushNtrip(byte[] data, int len)
    {
        Debug.LogFormat("pushing {0} bytes", len);

#if !UNITY_EDITOR
        AndroidJNI.AttachCurrentThread(); // something something, threads, android, dalwik, GC

        hiper.PushRTCM(data, len);

        AndroidJNI.DetachCurrentThread();
#endif

    }

    void OnApplicationQuit()
    {
        ntrip.Abort();
    }
}
