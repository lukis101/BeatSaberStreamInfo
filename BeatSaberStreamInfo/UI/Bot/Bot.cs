﻿using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace BeatSaberStreamInfo.UI.Bot
{
    public partial class Bot : Form
    {
        private static string botName;
        private static string channelName;
        private static string oauth;

        private readonly string[] cmds = { "!search", "!nowplaying", "!np" };

        private StreamWriter _writer;
        private bool _retry;
        private bool _exit;

        private BeatSaver bs;

        private Thread BotThread;

        public Bot()
        {
            InitializeComponent();
        }

        private void Bot_Load(object sender, EventArgs e)
        {
            bs = new BeatSaver();

            ReloadConfig();

            foreach (string l in File.ReadAllLines(Path.Combine(Plugin.dir, "data/botsettings.txt")))
            {
                if (l.StartsWith("cmd_search="))
                    check_cmdsearch.Checked = l.Replace("cmd_search=", "").ToLower() == "true";
                if (l.StartsWith("cmd_nowplaying="))
                    check_cmdnp.Checked = l.Replace("cmd_nowplaying=", "").ToLower() == "true";
                if (l.StartsWith("auto_nowplaying="))
                    check_nowplaying.Checked = l.Replace("auto_nowplaying=", "").ToLower() == "true";
                if (l.StartsWith("auto_endstats="))
                    check_endstats.Checked = l.Replace("auto_endstats=", "").ToLower() == "true";
            }
        }
        private void Bot_FormClosed(object sender, FormClosedEventArgs e)
        {
            string ret = "cmd_search=" + check_cmdsearch.Checked + Environment.NewLine +
                "cmd_nowplaying=" + check_cmdnp.Checked + Environment.NewLine +
                "auto_nowplaying=" + check_nowplaying + Environment.NewLine +
                "auto_endstats=" + check_endstats;
            File.WriteAllText(Path.Combine(Plugin.dir, "data/botsettings.txt"), ret);

        }

        private void button_connect_Click(object sender, EventArgs ev)
        {
            Log("Initializing...");

            if (botName == "" || channelName == "" || oauth == "" || !oauth.Contains("oauth:"))
            {
                Log("ERROR: Please make sure all info in your BotConfig.txt file is correct.");
                return;
            }

            BotThread = new Thread(new ThreadStart(Init));
            BotThread.Start();
        }
        private void button_disconnect_Click(object sender, EventArgs e)
        {
            _exit = true;
            BotThread.Abort();

            button_connect.Enabled = true;
            button_disconnect.Enabled = false;
            button_reload.Enabled = true;

            Log("Disconnected.");
            label_status.Text = "Status: Disconnected";
        }
        private void button_clear_Click(object sender, EventArgs e)
        {
            log.Text = "";
        }
        private void button_reload_Click(object sender, EventArgs e)
        {
            ReloadConfig();
            Log("Reloaded config"); 
        }

        private void ReloadConfig()
        {
            string[] lines = File.ReadAllLines(Path.Combine(Plugin.dir, "BotConfig.txt"));
            foreach (string l in lines)
            {
                if (l.StartsWith("BotName="))
                    botName = l.Replace("BotName=", "");
                if (l.StartsWith("ChannelName="))
                    channelName = l.Replace("ChannelName=", "");
                if (l.StartsWith("OAuth="))
                    oauth = l.Replace("OAuth=", "");
            }
        }
        private void Log(string s)
        {
            if (log.Text != "")
                log.AppendText(Environment.NewLine + "[" + DateTime.Now.ToString("hh:mm:ss tt") + "] " + s);
            else
                log.AppendText("[" + DateTime.Now.ToString("hh:mm:ss tt") + "] " + s);
        }
        private void Init()
        {
            var retryCount = 0;
            _exit = false;
            do
            {
                try
                {
                    Log("Connecting to twitch server.");
                    using (var irc = new TcpClient("irc.chat.twitch.tv", 6667))
                    using (var stream = irc.GetStream())
                    using (var reader = new StreamReader(stream))
                    using (var writer = new StreamWriter(stream))
                    {
                        // Set a global Writer
                        _writer = writer;

                        button_connect.Enabled = false;
                        button_disconnect.Enabled = true;
                        button_reload.Enabled = false;
                        // Login Information for the irc client
                        Log("Starting log in.");
                        SendMessage("PASS " + oauth);
                        SendMessage("NICK " + botName);
                        SendMessage("JOIN #" + channelName);
                        
                        SendMessage("CAP REQ :twitch.tv/membership");
                        SendMessage("CAP REQ :twitch.tv/commands");
                        SendMessage("CAP REQ :twitch.tv/tags");

                        Log("Connected.");
                        label_status.Text = "Status: Connected";

                        while (!_exit)
                        {
                            string inputLine;
                            while ((inputLine = reader.ReadLine()) != null || _exit)
                            {
                                //_logger.Debug(inputLine);

                                if (inputLine == null) continue;

                                var splitInput = inputLine.Split(' ');
                                if (splitInput[0] == "PING")
                                {
                                    //_logger.Info("Responded to twitch ping.");
                                    SendMessage("PONG " + splitInput[1]);
                                }

                                splitInput = inputLine.Split(':');

                                if (2 >= splitInput.Length) continue;

                                var msg = splitInput[2];
                                if (cmds.Contains(msg.Split(' ')[0]))
                                    ProcessCommand(msg);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    //_logger.Debug(e.ToString());
                    _retry = ++retryCount <= 20;
                    if (_exit)
                    {
                        _retry = false;
                    }
                    else
                    {
                        Thread.Sleep(5000);
                    }
                }
            } while (_retry);
        }
        private void SendMessage(String message)
        {
            if (message.Contains("PASS") || message.Contains("NICK") || message.Contains("JOIN #") || message.Contains("CAP REQ") || message.Contains("PONG"))
            {
                _writer.WriteLine(message);
            }
            else
                _writer.WriteLine("PRIVMSG #" + channelName + " :" + message);

            _writer.Flush();
        }
        private void ProcessCommand(string msg)
        {
            string[] split = msg.Split(new[] { ' ' }, 2);
            string command = split[0];

            if (!cmds.Contains(command))
                return;

            string args = "";
            if (split.Length == 2)
                args = split[1];

            if (command == "!search")
            {
                Log("Command triggered: " + msg);
                if (args != "")
                {
                    var results = bs.Search(args);
                    string response = "";
                    if (results.Count == 0)
                        response = $"🚫 No BeatSaver results for: \"" + args + "\"";
                    else
                    {
                        string s = "s";
                        if (results.Count == 1)
                            s = "";
                        string[] EmojiList = new[] { "1️⃣", "2️⃣", "3️⃣" };
                        response = $"✅ BeatSaver result" + s + " for: \"" + args + "\": " + EmojiList[0] + " " + results[0];
                        if (results.Count > 1)
                        {
                            for (int i = 1; i < results.Count(); i++)
                                response += " || " + EmojiList[i] + " " + results[i];
                        }
                    }

                    SendMessage(response);
                }
            }
        }
        public void SendNowPlaying(string song)
        {
            if (BotThread.IsAlive && check_nowplaying.Checked)
            {
                string response = "🎵 Now playing: " + song;
                SendMessage(response);
            }
        }
    }
}
