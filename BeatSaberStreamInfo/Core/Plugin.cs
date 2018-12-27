using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using UnityEngine;
using UnityEngine.SceneManagement;
using IllusionPlugin;

namespace BeatSaberStreamInfo
{
    public class Plugin : IPlugin
    {
        public string Name => "Beat Saber Stream Info";
        public string Version => "1.1.0";
        
        private AudioTimeSyncController ats;
        public static readonly string dir = Path.Combine(Environment.CurrentDirectory, "UserData/StreamInfo");
        private readonly string[] env = { "DefaultEnvironment", "BigMirrorEnvironment", "TriangleEnvironment", "NiceEnvironment" };

        private bool InSong;
        private bool EnergyReached0;
        public static int overlayRefreshRate;
        private SongInfo info;
        private Action StartJob;
        private HMTask OverlayTask;
        private HMTask StartTask;
        
        private Overlay overlay;

        private string _songName;
        private string _songAuthor;
        private string _songSub;

        private static int songCount = 0;

        private bool overlayEnabled = false;

        public static void Log(object message)
        {
            //string fullMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.FFF}] [StreamInfo] {message}";
            string fullMsg = $"[StreamInfo] {message}";
            Console.WriteLine(fullMsg);
            //Debug.Log(fullMsg);
        }

        public void OnApplicationStart()
        {
            SceneManager.activeSceneChanged += SceneManagerOnActiveSceneChanged;
            SceneManager.sceneLoaded += SceneManager_sceneLoaded;

            info = new SongInfo();

            if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "UserData")))
                Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "UserData"));
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            foreach (string s in new[] { "SongName", "overlaydata" })
            {
                if (!File.Exists(Path.Combine(dir, s + ".txt")))
                {
                    Log(s + ".txt not found. Creating file...");
                    if (s == "overlaydata")
                        File.WriteAllLines(Path.Combine(dir, s + ".txt"), new[] { "567,288", "0,40", "75,198", "307,134", "16,132", "87,19", "170,83", "303,19", "0,0" });
                    else
                        File.WriteAllText(Path.Combine(dir, s + ".txt"), "");
                }
            }
            if (ModPrefs.GetBool("StreamInfo", "OverlayEnabled", true, true))
            {
                Log("Launching overlay...");
                overlay = new Overlay();
                overlay.FormClosed += Overlay_FormClosed;
                Action overlayjob = delegate { System.Windows.Forms.Application.Run(overlay); };
                OverlayTask = new HMTask(overlayjob);
                OverlayTask.Run();
                overlay.Refresh();
                overlayRefreshRate = ModPrefs.GetInt("StreamInfo", "RefreshRate", 100, true);

                Log("Overlay started.");
                overlayEnabled = true;
            }
        }
        private void SceneManagerOnActiveSceneChanged(Scene arg0, Scene arg1)
        {
            if (overlayEnabled)
            {
                if (arg1.name == "Menu" && InSong)
                {
                    Log("Exited song scene.");

                    InSong = false;
                    StartTask.Cancel();
                    ats = null;

                    Log("Ready for next song.");
                }
                else if (env.Contains(arg1.name))
                {
                    StartJob = delegate
                    {
                        Log("Entered song scene. Initializing...");
                        InSong = true;
                        EnergyReached0 = false;
                        int runID = 1 + songCount++;

                        Log("Finding controllers and data...");

                        GameEnergyCounter energy = null;
                        ScoreController score = null;
                        StandardLevelSceneSetupDataSO setupData = null;

                        while (ats == null || energy == null || score == null || setupData == null)
                        {
                            Thread.Sleep(150);
                            ats = UnityEngine.Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().FirstOrDefault();
                            energy = UnityEngine.Resources.FindObjectsOfTypeAll<GameEnergyCounter>().FirstOrDefault();
                            score = UnityEngine.Resources.FindObjectsOfTypeAll<ScoreController>().FirstOrDefault();
                            setupData = UnityEngine.Resources.FindObjectsOfTypeAll<StandardLevelSceneSetupDataSO>().FirstOrDefault();
                        }
                        Log("Found controllers and data.");

                        bool noFail = false;

                        info.SetDefault();
                        if (setupData != null)
                        {
                            Log("Getting song name data...");
                            var level = setupData.difficultyBeatmap.level;

                            _songName = level.songName;
                            _songSub = level.songSubName;
                            _songAuthor = level.songAuthorName;

                            string songname = "\"" + _songName + "\" by " + _songSub + " - " + _songAuthor;
                            info.full_name = songname;
                            File.WriteAllText(Path.Combine(dir, "SongName.txt"), songname + "               ");

                            noFail = setupData.gameplayCoreSetupData.gameplayModifiers.noFail;
                        }
                        Log("Hooking Events...");
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
                        if (noFail)
                        {
                            EnergyReached0 = true;
                            info.energy = -3; 
                        }
                        Log("Starting update loop...");
                        while (InSong && overlayEnabled && runID == songCount)
                        {
                            if (ats != null)
                            {
                                string time = Math.Floor(ats.songTime / 60).ToString("N0") + ":" + Math.Floor(ats.songTime % 60).ToString("00");
                                string totaltime = Math.Floor(ats.songLength / 60).ToString("N0") + ":" + Math.Floor(ats.songLength % 60).ToString("00");
                                string percent = ((ats.songTime / ats.songLength) * 100).ToString("N0");

                                overlay.UpdateText(info.full_name,
                                        info.GetVal("multiplier"),
                                        info.GetVal("score"),
                                        ScoreController.MaxScoreForNumberOfNotes(info.notes_total),
                                        time + " / " + totaltime + " (" + percent + "%)",
                                        info.GetVal("combo"),
                                        info.GetVal("notes_hit") + "/" + info.GetVal("notes_total"),
                                        info.GetVal("energy"));
                            }
                            Thread.Sleep(overlayRefreshRate);
                        }
                        Log("Thread completed: " + runID);

                    };
                    StartTask = new HMTask(StartJob);
                    StartTask.Run();
                }
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
            if (data.noteType != NoteType.Bomb)
                info.notes_total++;
        }
        private void OnNoteCut(NoteData data, NoteCutInfo nci, int c)
        {
            if (data.noteType != NoteType.Bomb && nci.allIsOK)
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
            {
                if (f > 0)
                    info.energy = (int)(f * 100);
                else
                {
                    EnergyReached0 = true;
                    info.energy = -2;
                }
            }
        }
        private void OnEnergyFail()
        {
            if (!EnergyReached0)
            {
                EnergyReached0 = true;
                info.energy = -2;
            }
        }

        private void Overlay_FormClosed(object sender, FormClosedEventArgs e)
        {
            overlay = null;
            overlayEnabled = false;
        }

        public void OnApplicationQuit()
        {
            if (overlayEnabled && overlay != null)
            {
                overlay.ShutDown();
                OverlayTask.Cancel();
            }

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