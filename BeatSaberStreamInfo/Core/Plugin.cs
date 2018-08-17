﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using IllusionPlugin;
using IllusionInjector;
using System.IO;
using System.Threading;

namespace BeatSaberStreamInfo
{
    public class Plugin : IPlugin
    {
        public string Name => "Beat Saber Stream Info";
        public string Version => "1.0";

        private AudioTimeSyncController ats;
        private GameEnergyCounter energy;
        public static readonly string dir = Path.Combine(Environment.CurrentDirectory, "UserData/StreamInfo");
        private bool InSong;
        private bool EnergyReached0;
        private bool BailOutInstalled;
        private SongInfo info;
        Action job;
        HMTask writer;

        Overlay overlay;

        public void OnApplicationStart()
        {
            SceneManager.activeSceneChanged += SceneManagerOnActiveSceneChanged;
            SceneManager.sceneLoaded += SceneManager_sceneLoaded;

            BailOutInstalled = PluginManager.Plugins.Any(p => p.Name.Contains("Bail") && p.Name.Contains("Out") && p.Name.Contains("Mode"));
            if (BailOutInstalled)
                Console.WriteLine("[StreamInfo] BailOut plugin found.");
            else
                Console.WriteLine("[StreamInfo] BailOut plugin not found.");
            
            info = new SongInfo();
            
            job = delegate
            {
                var lastWritten = new Dictionary<string, string>();
                Console.WriteLine("[StreamInfo] HMTask started.");
                while (InSong)
                {
                    if (ats != null)
                    {
                        string time = Math.Floor(ats.songTime / 60).ToString("N0") + ":" + Math.Floor(ats.songTime % 60).ToString("00");
                        string totaltime = Math.Floor(ats.songLength / 60).ToString("N0") + ":" + Math.Floor(ats.songLength % 60).ToString("00");
                        string percent = ((ats.songTime / ats.songLength) * 100).ToString("N0");

                        overlay.UpdateText(info.GetVal("multiplier"),
                            info.GetVal("score"),
                            time + " / " + totaltime + " (" + percent + "%)",
                            info.GetVal("combo"),
                            info.GetVal("notes_hit") + "/" + info.GetVal("notes_total") + " (" + info.GetVal("percent") + "%)",
                            info.GetVal("energy"));
                    }
                    Thread.Sleep(100);
                }
            };
            writer = new HMTask(job);

            if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "UserData")))
                Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "UserData"));
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            List<string> sections = new List<string> { "SongName", "Config" };
            foreach (string s in sections)
                if (!File.Exists(Path.Combine(dir, s + ".txt")))
                {
                    Console.WriteLine("[StreamInfo] " + s + ".txt not found. Creating file...");
                    File.WriteAllText(Path.Combine(dir, s + ".txt"), "");
                }

            if (!File.Exists(Path.Combine(dir, "OverlayConfig.txt")))
            {
                Console.WriteLine("[StreamInfo] OverlayConfig.txt not found. Creating file...");
                File.WriteAllText(Path.Combine(dir, "OverlayConfig.txt"), "TextColor=White" + Environment.NewLine + "BackgroundColor=Black" + Environment.NewLine + "UseBackgroundImage=False");
            }
            
            overlay = new Overlay();
            Action overlayjob = delegate { System.Windows.Forms.Application.Run(overlay); };
            var OverlayTask = new HMTask(overlayjob);
            OverlayTask.Run();
        }
        private void SceneManagerOnActiveSceneChanged(Scene arg0, Scene arg1)
        {
            if (arg1.buildIndex == 5)
            {
                InSong = true;
                EnergyReached0 = false;
                ResetBailedOut();
                writer = new HMTask(job);
                writer.Run();

                // Get objects from scene to pull song data from.
                ats = UnityEngine.Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().FirstOrDefault();
                energy = UnityEngine.Resources.FindObjectsOfTypeAll<GameEnergyCounter>().FirstOrDefault();
                var score = UnityEngine.Object.FindObjectOfType<ScoreController>();
                var setupData = UnityEngine.Resources.FindObjectsOfTypeAll<MainGameSceneSetupData>().FirstOrDefault();
                
                string progress = "";
                
                if (setupData != null)
                {
                    var level = setupData.difficultyLevel.level;
                    
                    string songname = "\"" + level.songName + "\" by " + level.songSubName + " - " + level.songAuthorName;
                    File.WriteAllText(Path.Combine(dir, "SongName.txt"), songname + "               ");
                }
                if (ats != null)
                {
                    progress = "0:00/" + Math.Floor(ats.songLength / 60).ToString("N0") + ":" + Math.Floor(ats.songLength % 60).ToString("00") + " (0%)";
                }
                if (score != null)
                {
                    score.comboDidChangeEvent += OnComboChange;
                    score.multiplierDidChangeEvent += OnMultiplierChange;
                    score.noteWasMissedEvent += OnNoteMiss;
                    score.noteWasCutEvent += OnNoteCut;
                    score.scoreDidChangeEvent += OnScoreChange;
                }
                if (energy != null)
                {
                    energy.gameEnergyDidChangeEvent += OnEnergyChange;
                    energy.gameEnergyDidReach0Event += OnEnergyFail;
                }
                
                info.SetDefault();

                overlay.UpdateText(info.GetVal("multiplier"),
                            info.GetVal("score"),
                            progress,
                            info.GetVal("combo"),
                            info.GetVal("notes_hit") + "/" + info.GetVal("notes_total") + " (" + info.GetVal("percent") + "%)",
                            info.GetVal("energy"));
            }
            else
            {
                InSong = false;
                writer.Cancel();

                ats = null;
                energy = null;
            }
        }
         
        private void OnComboChange(int c)
        {
            info.combo = c;
        }
        private void OnMultiplierChange(int c, float f)
        {
            info.multiplier = c;
        }
        private void OnNoteMiss(NoteData data, int c)
        {
            info.combo = 0;
            info.notes_total++;
        }
        private void OnNoteCut(NoteData data, NoteCutInfo nci, int c)
        {
            if (nci.allIsOK)
            {
                info.notes_hit++;
                info.notes_total++;
            }
            else
                OnNoteMiss(data, c);
        }
        private void OnScoreChange(int c)
        {
            info.score = c;
        }

        private void OnEnergyChange(float f)
        {
            if (!EnergyReached0)
                info.energy = (int)(f * 100);
        }
        private void OnEnergyFail()
        {
            EnergyReached0 = true;
            File.WriteAllText(Path.Combine(dir, "Energy.txt"), "");
        }
                
        private void ResetBailedOut()
        {
            BailOutModePlugin.BailedOut = false;
        }
        private bool BailedOut()
        {
            return BailOutModePlugin.BailedOut;
        }

        public void OnApplicationQuit()
        {
            SceneManager.activeSceneChanged -= SceneManagerOnActiveSceneChanged;
            SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
        }
        public void OnUpdate() { }
        public void OnFixedUpdate() { }
        public void OnLevelWasLoaded(int level) { }
        public void OnLevelWasInitialized(int level) { }
        private void SceneManager_sceneLoaded(Scene arg0, LoadSceneMode arg1) { }
    }
}
