using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace RustedFair
{
    /// <summary>
    /// v1.0.0: iOS App Tracking Transparency. AdsManager asks once, after the UMP consent flow and before the first
    /// ad request (Assets/Plugins/iOS/RFTracking.mm). Android and the editor: always "authorized" (no ATT there;
    /// UMP consent alone decides). A status that is already decided is not asked again.
    /// </summary>
    public static class TrackingAuthorization
    {
        // ATTrackingManagerAuthorizationStatus: 0 notDetermined, 1 restricted, 2 denied, 3 authorized
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int RF_TrackingStatus();
        [DllImport("__Internal")] static extern void RF_TrackingRequest(string receiver);
#endif

        static Action<bool> pending;

        public static void Request(Action<bool> onDone)
        {
#if UNITY_IOS && !UNITY_EDITOR
            int st = RF_TrackingStatus();
            if (st != 0) { onDone?.Invoke(st == 3); return; }
            pending = onDone;
            var go = new GameObject(Receiver.Name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Receiver>();
            RF_TrackingRequest(Receiver.Name);
#else
            onDone?.Invoke(true);
#endif
        }

        /// <summary>Receives the native callback (UnitySendMessage) on the main thread.</summary>
        public class Receiver : MonoBehaviour
        {
            public const string Name = "[RF Tracking]";

            public void OnTrackingResult(string status)
            {
                int.TryParse(status, out int st);
                var cb = pending;
                pending = null;
                cb?.Invoke(st == 3);
                Destroy(gameObject);
            }
        }
    }
}
