using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace ThorEvidence.Core
{
    public sealed class PortInfo
    {
        public string PortName;
        public string Caption;
        public bool Recommended;
        public int RecommendationScore;

        public string DisplayName
        {
            get { return PortName + " - " + Caption + (Recommended ? " [推荐 B 通道]" : ""); }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public sealed class PortDiscoveryResult
    {
        public readonly List<PortInfo> Ports = new List<PortInfo>();
        public bool WmiAvailable;
        public string Diagnostic = "";
    }

    public static class PortDiscovery
    {
        public static PortDiscoveryResult Enumerate()
        {
            PortDiscoveryResult result = new PortDiscoveryResult();
            Dictionary<string, PortInfo> byPort = new Dictionary<string, PortInfo>(StringComparer.OrdinalIgnoreCase);
            bool wmiWorked = false;

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, DeviceID, Caption FROM Win32_SerialPort"))
                using (ManagementObjectCollection objects = searcher.Get())
                {
                    foreach (ManagementObject item in objects)
                    {
                        string port = ExtractPort(Convert.ToString(item["DeviceID"]));
                        if (String.IsNullOrEmpty(port)) port = ExtractPort(Convert.ToString(item["Name"]));
                        if (String.IsNullOrEmpty(port)) continue;
                        string caption = SanitizeCaption(Convert.ToString(item["Caption"]));
                        if (String.IsNullOrEmpty(caption)) caption = "Windows serial port";
                        byPort[port] = NewPort(port, caption);
                        wmiWorked = true;
                    }
                }

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, Caption FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                using (ManagementObjectCollection objects = searcher.Get())
                {
                    foreach (ManagementObject item in objects)
                    {
                        string name = Convert.ToString(item["Name"]);
                        string port = ExtractPort(name);
                        if (String.IsNullOrEmpty(port)) continue;
                        string caption = SanitizeCaption(name);
                        if (String.IsNullOrEmpty(caption)) caption = SanitizeCaption(Convert.ToString(item["Caption"]));
                        if (!byPort.ContainsKey(port)) byPort[port] = NewPort(port, caption);
                        else if (!String.IsNullOrEmpty(caption) && byPort[port].Caption == "Windows serial port") byPort[port].Caption = caption;
                        wmiWorked = true;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Diagnostic = "WMI enumeration unavailable: " + ex.Message;
            }

            try
            {
                string[] names = SerialPort.GetPortNames();
                for (int i = 0; i < names.Length; i++)
                {
                    string port = names[i].ToUpperInvariant();
                    if (!byPort.ContainsKey(port)) byPort[port] = NewPort(port, "SerialPort fallback");
                }
            }
            catch (Exception ex)
            {
                if (String.IsNullOrEmpty(result.Diagnostic)) result.Diagnostic = "SerialPort enumeration failed: " + ex.Message;
            }

            foreach (PortInfo info in byPort.Values) result.Ports.Add(info);
            result.Ports.Sort(delegate(PortInfo a, PortInfo b) { return ComparePortNames(a.PortName, b.PortName); });
            result.WmiAvailable = wmiWorked;
            if (!wmiWorked && String.IsNullOrEmpty(result.Diagnostic)) result.Diagnostic = "WMI did not return captions; only SerialPort.GetPortNames() was available.";
            if (result.Ports.Count == 0 && String.IsNullOrEmpty(result.Diagnostic)) result.Diagnostic = "No serial ports were discovered.";
            return result;
        }

        public static PortInfo Find(PortDiscoveryResult result, string portName)
        {
            if (result == null || String.IsNullOrEmpty(portName)) return null;
            for (int i = 0; i < result.Ports.Count; i++) if (String.Equals(result.Ports[i].PortName, portName, StringComparison.OrdinalIgnoreCase)) return result.Ports[i];
            return null;
        }

        private static PortInfo NewPort(string port, string caption)
        {
            string normalized = (caption ?? "").ToUpperInvariant();
            int score = 0;
            if (normalized.IndexOf("USB-ENHANCED-SERIAL-B", StringComparison.Ordinal) >= 0) score = 100;
            else if (normalized.IndexOf("CH342", StringComparison.Ordinal) >= 0 && normalized.IndexOf("B", StringComparison.Ordinal) >= 0) score = 90;
            else if (normalized.IndexOf("USB-ENHANCED-SERIAL-B", StringComparison.Ordinal) >= 0) score = 80;
            return new PortInfo { PortName = port.ToUpperInvariant(), Caption = caption, RecommendationScore = score, Recommended = score > 0 };
        }

        private static string ExtractPort(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            Match match = Regex.Match(value, "\\((COM[0-9]+)\\)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.ToUpperInvariant();
            match = Regex.Match(value, "^(COM[0-9]+)$", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";
        }

        public static string SanitizeCaption(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\r' || c == '\n' || c == '\t') builder.Append(' ');
                else if (Char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '(' || c == ')' || c == ':' || c == '.') builder.Append(c);
                else builder.Append('_');
            }
            return builder.ToString().Trim();
        }

        private static int ComparePortNames(string left, string right)
        {
            int a = PortNumber(left);
            int b = PortNumber(right);
            int compare = a.CompareTo(b);
            return compare != 0 ? compare : StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static int PortNumber(string value)
        {
            Match match = Regex.Match(value ?? "", "([0-9]+)$");
            int number;
            return match.Success && Int32.TryParse(match.Groups[1].Value, out number) ? number : Int32.MaxValue;
        }
    }
}
