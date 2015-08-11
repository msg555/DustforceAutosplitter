using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Timers;
using System.Xml;

namespace DustforceAutosplitter {
  public class Autosplitter {
    private class SplitData {
      public SplitData() {
        timer = new Timer();
        timer.Interval = 45;
        timer.AutoReset = false;
        pulses = 0;
        lastTime = 0;
      }

      public Timer timer;
      public int pulses;
      public long lastTime;
    }

    private Timer timer;
    private Dictionary<String, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>();
    private Dictionary<String, SplitData> splitData = new Dictionary<string, SplitData>();

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

    private static long getTimestamp() {
      var timeSpan = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0));
      return (long)timeSpan.TotalMilliseconds;
    }

    public Autosplitter() {
      XmlDocument configXml = new XmlDocument();
      try {
        configXml.Load("autosplitconfig.xml");
        foreach (XmlNode node in configXml.DocumentElement.SelectNodes("/config/path")) {
          tryPath(node.InnerText);
        }
      } catch (FileNotFoundException e) {
        Console.WriteLine("No config file found; using only defaults");
      }

      string homePath = (Environment.OSVersion.Platform == PlatformID.Unix ||
                         Environment.OSVersion.Platform == PlatformID.MacOSX)
               ? Environment.GetEnvironmentVariable("HOME")
               : Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
      tryPath(homePath + "\\AppData\\Roaming\\Dustforce\\user");

      string progFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
      tryPath(progFiles86 + "\\Steam\\steamapps\\common\\Dustforce\\user");

      string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
      if (progFiles != progFiles86) {
        tryPath(progFiles + "\\Steam\\steamapps\\common\\Dustforce\\user");
      }
      
      timer = new Timer();
      timer.Interval = 10000;
      timer.AutoReset = true;
      timer.Elapsed += (Object source, System.Timers.ElapsedEventArgs e) => {
        lock(watchers) {
          List<String> keys = new List<String>(watchers.Keys);
          foreach (String path in keys) {
            if (watchers[path] == null) {
              tryPath(path);
            }
          }
        }
      };
      timer.Enabled = true;
    }

    private void tryPath(String path) {
      if (!watchers.ContainsKey(path)) {
        Console.WriteLine("Checking " + path);
      }

      splitData[path] = new SplitData();
      if (!File.Exists(path + "\\stats0")) {
        watchers[path] = null;
        return;
      }
      Console.WriteLine("Start watch path " + path);

      FileSystemWatcher watcher = new FileSystemWatcher();
      watcher.Path = path;
      watcher.Changed += new FileSystemEventHandler(
        (object source, FileSystemEventArgs eargs) => {
          Console.WriteLine(eargs.Name);
          if (eargs.Name == "stats0" &&
              eargs.ChangeType == WatcherChangeTypes.Changed) {
            pulseSplit(path);
          }
        });
      watcher.Error += (object source, ErrorEventArgs e) => {
        Console.WriteLine("Lost directory " + path);
        lock(watchers) {
          watchers[path] = null;
        }
      };
      watcher.EnableRaisingEvents = true;
      watchers[path] = watcher;
    }

    private void pulseSplit(String path) {
      SplitData data = splitData[path];
      long now = getTimestamp();
      lock (data) {
        if (now - data.lastTime > 45) {
          data.pulses = 1;
        } else {
          data.pulses++;
          if (data.pulses == 2) {
            data.timer.Elapsed += (Object source, System.Timers.ElapsedEventArgs e) => {
              lock (data) {
                if (2 <= data.pulses && data.pulses <= 4) {
                  doSplit();
                }
              }
            };
            data.timer.Start();
          } else if (data.pulses == 5) {
            data.timer.Stop();
          }
        }
        data.lastTime = now;
      }
    }

    private void doSplit() {
      int VK_PRIOR = 0x21;
      keybd_event((byte)VK_PRIOR, 0, 1, 0);
      keybd_event((byte)VK_PRIOR, 0, 3, 0);
      Console.WriteLine("Split");
    }
  }

  class Program {
    static void Main(string[] args) {
      Autosplitter x = new Autosplitter();
      Console.WriteLine("Press enter to close");
      Console.Read();
    }
  }
}
