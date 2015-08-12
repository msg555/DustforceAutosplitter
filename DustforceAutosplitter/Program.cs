using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Timers;
using System.Xml;

namespace DustforceAutosplitter {
  public class Autosplitter {
    private class PathWatcher {
      private const int PULSE_WINDOW_MS = 45;

      private Autosplitter autosplitter;
      private String path;

      private int pulses;
      private DateTime lastPulseTime;
      private Timer splitTimer;
      private FileSystemWatcher fsWatcher;

      private readonly object syncLock = new object();

      public PathWatcher(Autosplitter autosplitter, String path) {
        this.autosplitter = autosplitter;
        this.path = path;

        splitTimer = new Timer();
        splitTimer.Interval = 45;
        splitTimer.AutoReset = false;
        splitTimer.Elapsed += (Object source, System.Timers.ElapsedEventArgs e) => {
          lock (syncLock) {
            if (2 <= pulses && pulses <= 4) {
              /* Finishing a level generates between 2 and 4 pulses. */
              autosplitter.doSplit();
            }
          }
        };
        pulses = 0;
        lastPulseTime = DateTime.Now;
        fsWatcher = null;
      }

      public void startWatching() {
        lock (syncLock) {
          if (fsWatcher != null || !File.Exists(path + "\\stats0")) {
            return;
          }
          Console.WriteLine("Start watch path " + path);

          /* Generate pulses based on changes to the stats0 file. */
          fsWatcher = new FileSystemWatcher();
          fsWatcher.Path = path;
          fsWatcher.Changed += new FileSystemEventHandler(
            (object source, FileSystemEventArgs eargs) => {
              if (eargs.Name == "stats0" &&
                  eargs.ChangeType == WatcherChangeTypes.Changed) {
                pulse();
              }
            });

          /* Test when we lose the monitored directory so we can later attempt
           * to begin watching again. */
          fsWatcher.Error += (object source, ErrorEventArgs e) => {
            Console.WriteLine("Lost directory " + path);
            lock (syncLock) {
              fsWatcher = null;
            }
          };
          fsWatcher.EnableRaisingEvents = true;
        }
      }

      /* This method is called every time a change event happens to stats0.
       * It adjusts the current pulse count subject to the pulse window and
       * schedules/unschedules a split based on the current pulse count.
       */
      private void pulse() {
        DateTime now = DateTime.Now;
        lock (syncLock) {
          if ((now - lastPulseTime).TotalMilliseconds > PULSE_WINDOW_MS) {
            /* The last pulse was long enough ago we should restart the pulse count. */
            pulses = 1;
          } else {
            pulses++;
            if (pulses == 2) {
              /* Finishing a level generates at least 2 pulses. */
              splitTimer.Start();
            } else if (pulses == 5) {
              /* Finishing a level generates no more than 4 pulses. */
              splitTimer.Stop();
            }
          }
          lastPulseTime = now;
        }
      }
    }

    private const int VK_PRIOR = 0x21; /* Page Up */
    private int splitVKKey = VK_PRIOR;

    private Timer rewatchTimer;
    private Dictionary<String, PathWatcher> watchers = new Dictionary<string, PathWatcher>();

    public Autosplitter() {
      /* Attempt to load the configuration file. */
      XmlDocument configXml = new XmlDocument();
      try {
        configXml.Load("autosplitconfig.xml");

        /* Start monitoring any paths from /config/path elements in the config. */
        foreach (XmlNode node in configXml.DocumentElement.SelectNodes("/config/path")) {
          initWatcher(node.InnerText);
        }

        /* Check for a custom split key. */
        XmlNode splitkeyNode = configXml.DocumentElement.SelectSingleNode("/config/splitkey");
        if (splitkeyNode != null) {
          if (int.TryParse(splitkeyNode.InnerText, out splitVKKey)) {
            Console.WriteLine("Using VK Key " + splitVKKey + " for splitting");
          } else {
            Console.WriteLine("Could not parse split key");
          }
        }
      } catch (FileNotFoundException e) {
        Console.WriteLine("No config file found; using only defaults");
      }

      /* Start monitoring the common Dustforce user paths. */
      string homePath = (Environment.OSVersion.Platform == PlatformID.Unix ||
                         Environment.OSVersion.Platform == PlatformID.MacOSX)
               ? Environment.GetEnvironmentVariable("HOME")
               : Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
      initWatcher(homePath + "\\AppData\\Roaming\\Dustforce\\user");

      string progFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
      initWatcher(progFiles86 + "\\Steam\\steamapps\\common\\Dustforce\\user");

      string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
      if (progFiles != progFiles86) {
        initWatcher(progFiles + "\\Steam\\steamapps\\common\\Dustforce\\user");
      }

      /* Create an additional timer that attempts to restart monitoring any folders
       * that didn't exist on startup or were deleted after startup.  This is needed
       * so if the user resets their save the autosplitter doesn't need to be
       * restarted.
       */
      rewatchTimer = new Timer();
      rewatchTimer.Interval = 10000;
      rewatchTimer.AutoReset = true;
      rewatchTimer.Elapsed += (Object source, System.Timers.ElapsedEventArgs e) => {
        lock(watchers) {
          /* Attempt to start monitoring all paths we know about that aren't already
           * being monitored.
           */
          foreach (PathWatcher watcher in watchers.Values) {
            watcher.startWatching();
          }
        }
      };
      rewatchTimer.Enabled = true;
    }

    private void initWatcher(String path) {
      if (watchers.ContainsKey(path)) {
        return;
      }

      Console.WriteLine("Adding " + path + " to watch list");
      watchers[path] = new PathWatcher(this, path);
      watchers[path].startWatching();
    }

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

    private void doSplit() {
      keybd_event((byte)splitVKKey, 0, 3, 0);
      keybd_event((byte)splitVKKey, 0, 1, 0);
      Console.WriteLine("Split");
     
    }
  }

  class Program {
    static void Main(string[] args) {
      /* Initialize the autosplitter and wait until the user exits. */
      Autosplitter x = new Autosplitter();
      Console.WriteLine("Press enter to close");
      Console.Read();
    }
  }
}
