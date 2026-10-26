using System;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Mita's face and gaze. Face changes (emotion textures and face layers) are host-authoritative and replayed on
    // clients by the same MitaPerson call. Gaze: wherever the game makes a character look at "the player", it looks at
    // the nearest player instead (on every machine, so everyone sees her look at the same person).
    // Idea credit: QuestionBlock64's MiSide-Multiplayer (re-implemented here; that project has no license).
    public sealed partial class TogetherRuntime
    {
        public const byte FaceEmotion = 1, FaceEmotionFast = 2, FaceEmotionOff = 3, FaceEmotionFastOff = 4, FaceLayer = 5, FaceLayerFast = 6;

        public static void AfterFace(MitaPerson mita, byte call, string text, int number)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Hosting || !rt.Connected || Replaying || mita == null) return;
            string key = SceneKeys.Key(mita);
            rt.Broadcast(Wire.Packet(Op.Face, w => { Wire.Text(w, key); w.Write(call); Wire.Text(w, text ?? ""); w.Write(number); }));
        }

        void ClientFace(BinaryReader reader)
        {
            string key = Wire.Text(reader, 1024);
            byte call = reader.ReadByte();
            string text = Wire.Text(reader, 128);
            int number = reader.ReadInt32();
            if (!ClientStoryLive) return;
            MitaPerson mita = SceneKeys.Find<MitaPerson>(key);
            if (mita == null) return;
            Replaying = true;
            try
            {
                switch (call)
                {
                    case FaceEmotion: mita.FaceEmotion(text); break;
                    case FaceEmotionFast: mita.FaceEmotionFast(text); break;
                    case FaceEmotionOff: mita.FaceEmotionOff(); break;
                    case FaceEmotionFastOff: mita.FaceEmotionFastOff(); break;
                    case FaceLayer: mita.FaceLayer(number); break;
                    case FaceLayerFast: mita.FaceLayerFast(number); break;
                }
                faceApplied++;
            }
            finally { Replaying = false; }
        }

        public int faceApplied;
        static bool redirecting;

        // The nearest player's head to this character, or null when that is the local player (the game's own call is right).
        public static Transform NearestRemoteHead(Character_Look look)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || redirecting || look == null) return null;
            Vector3 from = look.mainTransform != null ? look.mainTransform.position : look.transform.position;
            PlayerMove player = rt.LocalPlayer;
            float best = player != null ? (player.transform.position - from).sqrMagnitude : float.MaxValue;
            Transform head = null;
            foreach (Peer peer in rt.peers.Values)
            {
                RemoteAvatar avatar = peer.Avatar;
                if (avatar == null || avatar.Root == null || !avatar.Root.activeSelf || avatar.Head == null) continue;
                float d = (avatar.Root.transform.position - from).sqrMagnitude;
                if (d < best) { best = d; head = avatar.Head; }
            }
            return head;
        }

        // Runs the object-targeted variant of a "look at the player" call on the given head; returns false to skip the original.
        public static bool LookRedirect(Character_Look look, Action<Transform> call)
        {
            Transform head = NearestRemoteHead(look);
            if (head == null) return true;
            redirecting = true;
            try { call(head); gazeRedirects++; }
            finally { redirecting = false; }
            return false;
        }

        public static int gazeRedirects;
    }
}
