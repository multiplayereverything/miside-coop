using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Co-op saves: clients continue from the host's saves (mirrored into their Save folder, their own saves backed up
    // and restored), and the GlobalGame statics the menu sets before loading a chapter travel with the scene announcement.
    public sealed partial class TogetherRuntime
    {
        // MiSide keeps its saves as small text files in <persistentDataPath>/Save (Continue = chapter + counters).
        // GlobalGame.dataFiles is NOT the save folder (it is the Data folder with languages and voices).
        static string SaveDir { get { return Path.Combine(Application.persistentDataPath, "Save"); } }
        static string BackupDir { get { return Path.Combine(Application.persistentDataPath, "MiSideTogether", "save-backup"); } }
        static string BackupMarker { get { return Path.Combine(BackupDir, ".active"); } }
        readonly Dictionary<string, string> saveHashes = new Dictionary<string, string>();
        float nextSaveScan;

        // A client's own saves are backed up while it plays the host's game and restored afterwards (or at the next
        // start, after a crash). Lab copies share the user's folder, so scripts/run-tour.ps1 backs it up per run instead.
        void BackupSaves()
        {
            if (LabMode || SecondCopy || File.Exists(BackupMarker)) return;
            try
            {
                if (Directory.Exists(BackupDir)) Directory.Delete(BackupDir, true);
                Directory.CreateDirectory(BackupDir);
                if (Directory.Exists(SaveDir)) foreach (string file in Directory.GetFiles(SaveDir)) File.Copy(file, Path.Combine(BackupDir, Path.GetFileName(file)), true);
                File.WriteAllText(BackupMarker, DateTime.Now.ToString("o"));
                Log("Saves: your own saves are backed up while you play in someone else's game");
            }
            catch (Exception ex) { Log("Saves: backup failed: " + ex.Message); }
        }

        public static void RestoreSaves()
        {
            if (LabMode || SecondCopy || !File.Exists(BackupMarker)) return;
            try
            {
                Directory.CreateDirectory(SaveDir);
                foreach (string file in Directory.GetFiles(SaveDir)) File.Delete(file);
                foreach (string file in Directory.GetFiles(BackupDir)) if (Path.GetFileName(file) != ".active") File.Copy(file, Path.Combine(SaveDir, Path.GetFileName(file)), true);
                File.Delete(BackupMarker);
                Log("Saves: your own saves are restored");
            }
            catch (Exception ex) { Log("Saves: restore failed: " + ex.Message); }
        }

        // Host: any change in its Save folder is mirrored to the clients (checked every 2 s; the files are tiny).
        void HostSaveScan()
        {
            float now = Time.realtimeSinceStartup;
            if (!Hosting || now < nextSaveScan || !Directory.Exists(SaveDir)) return;
            nextSaveScan = now + 2;
            foreach (string file in Directory.GetFiles(SaveDir))
            {
                if (new FileInfo(file).Length > 16384) continue;
                string text = File.ReadAllText(file), name = Path.GetFileName(file);
                string hash = Wire.Hash(text).ToString();
                string old;
                if (saveHashes.TryGetValue(name, out old) && old == hash) continue;
                saveHashes[name] = hash;
                if (old != null || AcceptedCount > 0) Broadcast(SaveFilePacket(name, text));
                if (LabMode && old != null) Log("Saves: host save changed: " + name);
            }
        }

        static byte[] SaveFilePacket(string name, string text)
        {
            return Wire.Packet(Op.SaveFile, w => { Wire.Text(w, name); w.Write(text != null); if (text != null) Wire.Text(w, text); });
        }

        // Sent to each client as it joins: every save file the host has, so the client continues from the same state.
        void SendSavesTo(int peer)
        {
            if (!Directory.Exists(SaveDir)) return;
            int files = 0;
            foreach (string file in Directory.GetFiles(SaveDir))
            {
                if (new FileInfo(file).Length > 16384 || files >= 64) continue;
                Network.Send(peer, SaveFilePacket(Path.GetFileName(file), File.ReadAllText(file)));
                files++;
            }
            Log("Sent " + files + " save file(s) to player " + peer);
        }

        void ClientSaveFile(BinaryReader reader)
        {
            string name = Wire.Text(reader, 128);
            bool has = reader.ReadBoolean();
            string text = has ? Wire.Text(reader, 16384) : null;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith(".")) return;
            // A second copy on the same PC shares the save folder with the main game: it must not write there.
            if (SecondCopy) return;
            BackupSaves();
            Directory.CreateDirectory(SaveDir);
            string path = Path.Combine(SaveDir, name);
            if (has) File.WriteAllText(path, text); else if (File.Exists(path)) File.Delete(path);
            Log("Saves: mirrored host save " + name);
        }
        // --- GlobalGame statics -------------------------------------------------------------------------
        sealed class GameStatics
        {
            public string LoadingLevel = "", NameLoadedScene = "", NamePlayer = "", ClothMita = "";
            public int LevelLoad, ClothVariantMita;
            public bool OptionGameLoad, PlayWorld, Play, GameEndingMenu;

            public static GameStatics Capture()
            {
                return new GameStatics
                {
                    LoadingLevel = GlobalGame.LoadingLevel ?? "", NameLoadedScene = GlobalGame.nameLoadedScene ?? "", NamePlayer = GlobalGame.namePlayer ?? "",
                    ClothMita = GlobalGame.clothMita ?? "", LevelLoad = GlobalGame.levelLoad, ClothVariantMita = GlobalGame.clothVariantMita,
                    OptionGameLoad = GlobalGame.optionGameLoad, PlayWorld = GlobalGame.playWorld, Play = GlobalGame.play, GameEndingMenu = GlobalGame.gameEndingMenu
                };
            }

            public void Write(BinaryWriter w)
            {
                Wire.Text(w, LoadingLevel); Wire.Text(w, NameLoadedScene); Wire.Text(w, NamePlayer); Wire.Text(w, ClothMita);
                w.Write(LevelLoad); w.Write(ClothVariantMita); w.Write(OptionGameLoad); w.Write(PlayWorld); w.Write(Play); w.Write(GameEndingMenu);
            }

            public static GameStatics Read(BinaryReader r)
            {
                return new GameStatics
                {
                    LoadingLevel = Wire.Text(r, 128), NameLoadedScene = Wire.Text(r, 128), NamePlayer = Wire.Text(r, 64), ClothMita = Wire.Text(r, 128),
                    LevelLoad = r.ReadInt32(), ClothVariantMita = r.ReadInt32(), OptionGameLoad = r.ReadBoolean(), PlayWorld = r.ReadBoolean(), Play = r.ReadBoolean(), GameEndingMenu = r.ReadBoolean()
                };
            }

            // Name and Mita's outfit always follow the host; the load statics only matter for the next chapter load.
            public void ApplyIdentity()
            {
                if (NamePlayer.Length > 0) GlobalGame.namePlayer = NamePlayer;
                if (ClothMita.Length > 0) { GlobalGame.clothMita = ClothMita; GlobalGame.clothVariantMita = ClothVariantMita; }
            }

            // fromChapter: this machine is coming from a chapter too. playWorld means "carry on from the chapter before";
            // coming from the menu (a player who waited for this chapter) it must be off, as when the menu starts one,
            // or the player never initialises (PlayerMove throws every frame).
            public void ApplyLoad(bool fromChapter)
            {
                ApplyIdentity();
                GlobalGame.LoadingLevel = LoadingLevel; GlobalGame.nameLoadedScene = NameLoadedScene; GlobalGame.levelLoad = LevelLoad;
                GlobalGame.optionGameLoad = OptionGameLoad; GlobalGame.playWorld = PlayWorld && fromChapter; GlobalGame.play = Play; GlobalGame.gameEndingMenu = GameEndingMenu;
            }

            public override string ToString() { return "level '" + LoadingLevel + "' #" + LevelLoad + " loaded '" + NameLoadedScene + "' optionLoad=" + OptionGameLoad + " playWorld=" + PlayWorld + " name '" + NamePlayer + "' cloth '" + ClothMita + "'/" + ClothVariantMita; }
        }

        // Host: what the menu (or a story transition) set just before the loading screen; sent with each scene.
        GameStatics hostLoadStatics, clientLoadStatics;

        void CaptureLoadStatics(string active)
        {
            if (active == "SceneLoading") { hostLoadStatics = GameStatics.Capture(); if (LabMode) Log("Statics at load: " + hostLoadStatics); }
        }
    }
}
