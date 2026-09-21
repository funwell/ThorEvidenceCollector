using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ThorEvidence.Core
{
    public static class CommandPolicy
    {
        private static readonly string[] _allowed =
        {
            "version",
            "help",
            "showvoltages",
            "pmstateget",
            "readtemp",
            "pmrunstate",
            "pncstatus",
            "socstatus",
            "readvolt",
            "readvrs12",
            "swtlinkstatus",
            "swtSqiValue",
            "swtCrcCount",
            "swtstatusdata"
        };

        public static string[] AllowedCommands
        {
            get { return (string[])_allowed.Clone(); }
        }

        public static bool IsAllowed(string command)
        {
            if (String.IsNullOrEmpty(command) || !String.Equals(command, command.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = 0; i < command.Length; i++)
            {
                char c = command[i];
                if (c == '\r' || c == '\n' || c == ';' || c == '|' || c == '&' || c == '\t')
                {
                    return false;
                }
            }

            for (int i = 0; i < _allowed.Length; i++)
            {
                if (String.Equals(_allowed[i], command, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsForbidden(string command)
        {
            return !IsAllowed(command);
        }

        public static string ToWireCommand(string command)
        {
            if (!IsAllowed(command))
            {
                throw new InvalidOperationException("Command rejected by the read-only allowlist.");
            }

            return command + "\r\n";
        }
    }

    public sealed class EvidenceSnapshot
    {
        public string McuVersion = "";
        public string McuA = "";
        public string BuildDate = "";
        public string PlatformCommitId = "";
        public string DefaultBootChain = "";
        public string NextBootChain = "";
        public string BomId = "";
        public string BoardBomId = "";

        public bool NvShellSeen;
        public bool PromptSeen;
        public bool BootBannerSeen;
        public int BootCycleCount;

        public string Kl30Vbat = "";
        public string Vdd12 = "";
        public string VbatSoc = "";
        public string Acc1 = "";
        public string Vrs5 = "";
        public string Prereg16 = "";
        public string Prereg5 = "";
        public string Serdes18 = "";
        public string Serdes12 = "";
        public string Serdes10 = "";
        public string SocUfs = "";
        public string HsPower2 = "";
        public string PgPwrSerdes = "";
        public string PgSocVrs11 = "";
        public string SocPgLcvr = "";
        public string SocPgLcvrAo = "";

        public string McuTemp = "";
        public string SocTemp = "";
        public string CoolantTemp = "";
        public string WakeSource = "";
        public string PowerDeviceStatus = "";
        public string PmState = "";
        public readonly List<string> PowerTransitions = new List<string>();

        public bool PowerUpRequested;
        public bool PowerDownRequested;
        public readonly List<string> PowerDownReasonLines = new List<string>();
        public string DtcSocLink = "";
        public string DtcDhuLink = "";
        public bool SocTempReadFailed;

        public readonly List<string> CommandsAttempted = new List<string>();
        public readonly List<string> CommandsTimedOut = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Events = new List<string>();

        public void AddWarning(string warning)
        {
            if (!String.IsNullOrEmpty(warning) && !Warnings.Contains(warning))
            {
                Warnings.Add(warning);
            }
        }

        public void AddEvent(string item)
        {
            if (!String.IsNullOrEmpty(item))
            {
                Events.Add(item);
            }
        }
    }

    public sealed class EvidenceParser
    {
        private readonly StringBuilder _text = new StringBuilder();

        public void Append(string text)
        {
            if (!String.IsNullOrEmpty(text))
            {
                _text.Append(text);
            }
        }

        public string Text
        {
            get { return _text.ToString(); }
        }

        public EvidenceSnapshot Snapshot()
        {
            string text = _text.ToString();
            EvidenceSnapshot snapshot = new EvidenceSnapshot();

            snapshot.McuVersion = LastGroup(text, "mcu_version\\s*[:=]\\s*([^\\]\\r\\n]+)");
            snapshot.McuA = LastGroup(text, "mcuA\\s+Version\\s+([^\\s\\r\\n]+)");
            snapshot.BuildDate = LastGroup(text, "build_date\\s*[:=]\\s*([^\\]\\r\\n]+)");
            snapshot.PlatformCommitId = LastGroup(text, "(?:platform[_ ]?commit(?:id)?|commit[_ ]?id)\\s*[:=]\\s*([^\\]\\r\\n]+)");
            snapshot.DefaultBootChain = LastGroup(text, "DefaultBootChain\\s*[:=]\\s*([^,\\]\\r\\n]+)");
            snapshot.NextBootChain = LastGroup(text, "NextBootChain\\s*[:=]\\s*([^,\\]\\r\\n]+)");
            snapshot.BomId = LastGroup(text, "(?:^|[\\[, ]+)BomID\\s*[:=]\\s*([^,\\]\\r\\n]+)");
            snapshot.BoardBomId = LastGroup(text, "Board_BomID\\s*[:=]\\s*([^,\\]\\r\\n]+)");

            snapshot.BootBannerSeen = Regex.IsMatch(text, "NvShell\\s+Initialization\\s+Start", RegexOptions.IgnoreCase);
            snapshot.NvShellSeen = Regex.IsMatch(text, "NvShell", RegexOptions.IgnoreCase);
            snapshot.PromptSeen = Regex.IsMatch(text, "NvShell\\s*>", RegexOptions.IgnoreCase);
            snapshot.BootCycleCount = Count(text, "NvShell\\s+Initialization\\s+Start");

            snapshot.Kl30Vbat = Voltage(text, "SENSE_KL30_VBAT");
            snapshot.Vdd12 = Voltage(text, "SENSE_VDD_12V");
            snapshot.VbatSoc = Voltage(text, "SENSE_VBAT_SOC");
            snapshot.Acc1 = Voltage(text, "SENSE_ACC1");
            snapshot.Vrs5 = Voltage(text, "SENSE_VRS_5V_OR_SOCVCC");
            snapshot.Prereg16 = Voltage(text, "SENSE_PREREG_16V");
            snapshot.Prereg5 = Voltage(text, "SENSE_PREREG_5V");
            snapshot.Serdes18 = Voltage(text, "SENSE_SERDES_1V8");
            snapshot.Serdes12 = Voltage(text, "SENSE_SERDES_1V2");
            snapshot.Serdes10 = Voltage(text, "SENSE_SERDES_1V0");
            snapshot.SocUfs = Voltage(text, "SENSE_SOC_PPVCC_UFS");
            snapshot.HsPower2 = Voltage(text, "SENSE_HS_POWER2");
            snapshot.PgPwrSerdes = Voltage(text, "PG_PWR_SERDES");
            snapshot.PgSocVrs11 = Voltage(text, "PG_SOC_VRS11");
            snapshot.SocPgLcvr = Voltage(text, "SOC_PG_LCVR");
            snapshot.SocPgLcvrAo = Voltage(text, "SOC_PG_LCVR_AO");

            snapshot.McuTemp = Temperature(text, "(?:mcu_temp|MCU_temp)");
            snapshot.SocTemp = Temperature(text, "(?:T_Soc|soc_temp)");
            snapshot.CoolantTemp = Temperature(text, "(?:T_Cool|coolant_temp)");
            snapshot.WakeSource = LastGroup(text, "Cur\\s+wake[- ]up\\s+src\\s*:\\s*([^\\s,\\]\\r\\n]+)");
            snapshot.PowerDeviceStatus = LastLine(text, "(?:Power_low_Mode|ElectricalPower_UB|car_mode_UB|usage_mode_UB)");
            snapshot.PmState = LastGroup(text, "PM_StateM\\s*:\\s*([^\\r\\n]+)");

            MatchCollection transitions = Regex.Matches(text, "PM_StateM\\s*:\\s*([A-Za-z0-9_]+\\s*->\\s*[A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
            for (int i = 0; i < transitions.Count; i++)
            {
                string transition = transitions[i].Groups[1].Value.Trim();
                if (!snapshot.PowerTransitions.Contains(transition))
                {
                    snapshot.PowerTransitions.Add(transition);
                }
            }

            snapshot.PowerUpRequested = Regex.IsMatch(text, "MCU_PLTFPWRMGR_REQ_POWERON|Powering\\s+on|PM_StateM\\s*:\\s*[^\\r\\n]*INIT", RegexOptions.IgnoreCase);
            snapshot.PowerDownRequested = Regex.IsMatch(text, "MCU_PLTFPWRMGR_REQ_POWERDOWN|Powerdown\\s+requested|Powering\\s+off", RegexOptions.IgnoreCase);
            MatchCollection reasonMatches = Regex.Matches(text, "(?im)^.*(?:MCU_PLTFPWRMGR_REQ_POWERDOWN|Powerdown\\s+requested|Powering\\s+off|STANDBY\\s*->\\s*SLEEP).*$");
            for (int i = 0; i < reasonMatches.Count; i++)
            {
                string line = reasonMatches[i].Value.Trim();
                if (!snapshot.PowerDownReasonLines.Contains(line))
                {
                    snapshot.PowerDownReasonLines.Add(line);
                }
            }

            snapshot.DtcSocLink = LastGroup(text, "DTC_SOC\\s+([^\\s\\r\\n]+)");
            snapshot.DtcDhuLink = LastGroup(text, "DTC_DHU\\s+([^\\s\\r\\n]+)");
            snapshot.SocTempReadFailed = Regex.IsMatch(text, "soc(?:_temp|\\s+get\\s+temp).*?(?:fail|error)", RegexOptions.IgnoreCase);

            if (IsNearZero(snapshot.Acc1))
            {
                snapshot.AddWarning("ACC1 is 0 V in at least one sample");
            }

            if (AllNearZero(snapshot.Serdes18, snapshot.Serdes12, snapshot.Serdes10, snapshot.SocUfs))
            {
                snapshot.AddWarning("SoC/SerDes/UFS rails remain near zero");
            }

            if (snapshot.PowerDownRequested)
            {
                snapshot.AddWarning("MCU requested powerdown");
            }

            if (snapshot.WakeSource == "0")
            {
                snapshot.AddWarning("wake source is zero");
            }

            if (!snapshot.BootBannerSeen)
            {
                snapshot.AddWarning("boot banner not captured");
            }

            if (!snapshot.PromptSeen)
            {
                snapshot.AddWarning("prompt not captured");
            }

            return snapshot;
        }

        private static string Voltage(string text, string name)
        {
            return LastGroup(text, Regex.Escape(name) + "\\s*=\\s*([-+]?[0-9]+(?:\\.[0-9]+)?)\\s*V");
        }

        private static string Temperature(string text, string name)
        {
            return LastGroup(text, name + "\\s*[:=]\\s*([-+]?[0-9]+(?:\\.[0-9]+)?)\\s*(?:C|°C)?");
        }

        private static string LastGroup(string text, string pattern)
        {
            MatchCollection matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
            return matches.Count == 0 ? "" : matches[matches.Count - 1].Groups[1].Value.Trim();
        }

        private static string LastLine(string text, string pattern)
        {
            MatchCollection matches = Regex.Matches(text, "(?im)^.*" + pattern + ".*$");
            return matches.Count == 0 ? "" : matches[matches.Count - 1].Value.Trim();
        }

        private static int Count(string text, string pattern)
        {
            return Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count;
        }

        private static bool IsNearZero(string value)
        {
            double number;
            return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && Math.Abs(number) <= 0.05;
        }

        private static bool AllNearZero(params string[] values)
        {
            bool found = false;
            for (int i = 0; i < values.Length; i++)
            {
                if (!String.IsNullOrEmpty(values[i]))
                {
                    found = true;
                    if (!IsNearZero(values[i]))
                    {
                        return false;
                    }
                }
            }

            return found;
        }
    }

    public static class JsonUtil
    {
        public static string Escape(string value)
        {
            if (value == null)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            return builder.ToString();
        }

        public static string StringValue(string value)
        {
            return "\"" + Escape(value) + "\"";
        }

        public static string BoolValue(bool value)
        {
            return value ? "true" : "false";
        }

        public static string IntValue(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static void Property(StringBuilder builder, ref bool first, string name, string value)
        {
            if (!first)
            {
                builder.Append(',');
            }
            first = false;
            builder.Append(StringValue(name));
            builder.Append(':');
            builder.Append(value);
        }

        public static string StringArray(IEnumerable<string> values)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('[');
            bool first = true;
            if (values != null)
            {
                foreach (string value in values)
                {
                    if (!first) builder.Append(',');
                    first = false;
                    builder.Append(StringValue(value));
                }
            }
            builder.Append(']');
            return builder.ToString();
        }
    }

    public static class EvidenceFormatter
    {
        public static string BuildSummary(EvidenceSnapshot snapshot, string port, string caption, bool synthetic, string[] commands)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Thor NvShell 证据采集摘要");
            builder.AppendLine("========================");
            builder.AppendLine("证据类型: " + (synthetic ? "synthetic replay/demo（非真实硬件）" : "serial capture"));
            builder.AppendLine("串口: " + (port ?? ""));
            builder.AppendLine("设备描述: " + (caption ?? ""));
            builder.AppendLine("MCU: " + snapshot.McuVersion);
            builder.AppendLine("mcuA: " + snapshot.McuA);
            builder.AppendLine("NvShell: " + (snapshot.NvShellSeen ? "seen" : "not seen") + ", prompt: " + (snapshot.PromptSeen ? "seen" : "not seen"));
            builder.AppendLine("Boot cycles: " + snapshot.BootCycleCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("PM state: " + snapshot.PmState);
            builder.AppendLine("PM transitions: " + String.Join(" | ", snapshot.PowerTransitions.ToArray()));
            builder.AppendLine("Wake source: " + snapshot.WakeSource);
            builder.AppendLine("KL30/VBAT: " + snapshot.Kl30Vbat + " V, VDD12: " + snapshot.Vdd12 + " V, ACC1: " + snapshot.Acc1 + " V");
            builder.AppendLine("SoC/SerDes/UFS: " + snapshot.Serdes18 + " / " + snapshot.Serdes12 + " / " + snapshot.Serdes10 + " / " + snapshot.SocUfs + " V");
            builder.AppendLine("温度: MCU " + snapshot.McuTemp + " C, SoC " + snapshot.SocTemp + " C, coolant " + snapshot.CoolantTemp + " C");
            builder.AppendLine("power-up requested: " + snapshot.PowerUpRequested + ", power-down requested: " + snapshot.PowerDownRequested);
            builder.AppendLine("只读命令: " + String.Join(", ", (commands ?? new string[0])));
            builder.AppendLine("已尝试: " + String.Join(", ", snapshot.CommandsAttempted.ToArray()));
            builder.AppendLine("超时: " + String.Join(", ", snapshot.CommandsTimedOut.ToArray()));
            builder.AppendLine("警告:");
            if (snapshot.Warnings.Count == 0)
            {
                builder.AppendLine("  none");
            }
            else
            {
                for (int i = 0; i < snapshot.Warnings.Count; i++) builder.AppendLine("  - " + snapshot.Warnings[i]);
            }
            builder.AppendLine();
            builder.AppendLine("安全策略：仅发送只读命令（包括提示所需的 CRLF），不发送 poweron/复位/刷写/CAN 命令。");
            return builder.ToString();
        }

        public static string BuildJson(EvidenceSnapshot snapshot, string port, string caption, bool synthetic, string[] commands)
        {
            StringBuilder builder = new StringBuilder();
            bool first = true;
            builder.Append('{');
            JsonUtil.Property(builder, ref first, "schemaVersion", JsonUtil.StringValue("1.0"));
            JsonUtil.Property(builder, ref first, "synthetic", JsonUtil.BoolValue(synthetic));
            JsonUtil.Property(builder, ref first, "port", JsonUtil.StringValue(port));
            JsonUtil.Property(builder, ref first, "caption", JsonUtil.StringValue(caption));
            AddSnapshotProperties(builder, ref first, snapshot);
            JsonUtil.Property(builder, ref first, "commandsAllowed", JsonUtil.StringArray(commands));
            JsonUtil.Property(builder, ref first, "commandsAttempted", JsonUtil.StringArray(snapshot.CommandsAttempted));
            JsonUtil.Property(builder, ref first, "commandsTimedOut", JsonUtil.StringArray(snapshot.CommandsTimedOut));
            JsonUtil.Property(builder, ref first, "warnings", JsonUtil.StringArray(snapshot.Warnings));
            JsonUtil.Property(builder, ref first, "events", JsonUtil.StringArray(snapshot.Events));
            builder.Append('}');
            return builder.ToString();
        }

        internal static void AddSnapshotProperties(StringBuilder builder, ref bool first, EvidenceSnapshot snapshot)
        {
            JsonUtil.Property(builder, ref first, "mcuVersion", JsonUtil.StringValue(snapshot.McuVersion));
            JsonUtil.Property(builder, ref first, "mcuA", JsonUtil.StringValue(snapshot.McuA));
            JsonUtil.Property(builder, ref first, "buildDate", JsonUtil.StringValue(snapshot.BuildDate));
            JsonUtil.Property(builder, ref first, "platformCommitId", JsonUtil.StringValue(snapshot.PlatformCommitId));
            JsonUtil.Property(builder, ref first, "defaultBootChain", JsonUtil.StringValue(snapshot.DefaultBootChain));
            JsonUtil.Property(builder, ref first, "nextBootChain", JsonUtil.StringValue(snapshot.NextBootChain));
            JsonUtil.Property(builder, ref first, "bomId", JsonUtil.StringValue(snapshot.BomId));
            JsonUtil.Property(builder, ref first, "boardBomId", JsonUtil.StringValue(snapshot.BoardBomId));
            JsonUtil.Property(builder, ref first, "nvShellSeen", JsonUtil.BoolValue(snapshot.NvShellSeen));
            JsonUtil.Property(builder, ref first, "promptSeen", JsonUtil.BoolValue(snapshot.PromptSeen));
            JsonUtil.Property(builder, ref first, "bootBannerSeen", JsonUtil.BoolValue(snapshot.BootBannerSeen));
            JsonUtil.Property(builder, ref first, "bootCycleCount", JsonUtil.IntValue(snapshot.BootCycleCount));
            AddVoltageProperties(builder, ref first, snapshot);
            JsonUtil.Property(builder, ref first, "mcuTemp", JsonUtil.StringValue(snapshot.McuTemp));
            JsonUtil.Property(builder, ref first, "socTemp", JsonUtil.StringValue(snapshot.SocTemp));
            JsonUtil.Property(builder, ref first, "coolantTemp", JsonUtil.StringValue(snapshot.CoolantTemp));
            JsonUtil.Property(builder, ref first, "wakeSource", JsonUtil.StringValue(snapshot.WakeSource));
            JsonUtil.Property(builder, ref first, "powerDeviceStatus", JsonUtil.StringValue(snapshot.PowerDeviceStatus));
            JsonUtil.Property(builder, ref first, "pmState", JsonUtil.StringValue(snapshot.PmState));
            JsonUtil.Property(builder, ref first, "pmTransitions", JsonUtil.StringArray(snapshot.PowerTransitions));
            JsonUtil.Property(builder, ref first, "powerUpRequested", JsonUtil.BoolValue(snapshot.PowerUpRequested));
            JsonUtil.Property(builder, ref first, "powerDownRequested", JsonUtil.BoolValue(snapshot.PowerDownRequested));
            JsonUtil.Property(builder, ref first, "powerDownReasonLines", JsonUtil.StringArray(snapshot.PowerDownReasonLines));
            JsonUtil.Property(builder, ref first, "dtcSocLink", JsonUtil.StringValue(snapshot.DtcSocLink));
            JsonUtil.Property(builder, ref first, "dtcDhuLink", JsonUtil.StringValue(snapshot.DtcDhuLink));
            JsonUtil.Property(builder, ref first, "socTempReadFailed", JsonUtil.BoolValue(snapshot.SocTempReadFailed));
        }

        private static void AddVoltageProperties(StringBuilder builder, ref bool first, EvidenceSnapshot snapshot)
        {
            JsonUtil.Property(builder, ref first, "kl30Vbat", JsonUtil.StringValue(snapshot.Kl30Vbat));
            JsonUtil.Property(builder, ref first, "vdd12", JsonUtil.StringValue(snapshot.Vdd12));
            JsonUtil.Property(builder, ref first, "vbatSoc", JsonUtil.StringValue(snapshot.VbatSoc));
            JsonUtil.Property(builder, ref first, "acc1", JsonUtil.StringValue(snapshot.Acc1));
            JsonUtil.Property(builder, ref first, "vrs5", JsonUtil.StringValue(snapshot.Vrs5));
            JsonUtil.Property(builder, ref first, "prereg16", JsonUtil.StringValue(snapshot.Prereg16));
            JsonUtil.Property(builder, ref first, "prereg5", JsonUtil.StringValue(snapshot.Prereg5));
            JsonUtil.Property(builder, ref first, "serdes18", JsonUtil.StringValue(snapshot.Serdes18));
            JsonUtil.Property(builder, ref first, "serdes12", JsonUtil.StringValue(snapshot.Serdes12));
            JsonUtil.Property(builder, ref first, "serdes10", JsonUtil.StringValue(snapshot.Serdes10));
            JsonUtil.Property(builder, ref first, "socUfs", JsonUtil.StringValue(snapshot.SocUfs));
            JsonUtil.Property(builder, ref first, "hsPower2", JsonUtil.StringValue(snapshot.HsPower2));
            JsonUtil.Property(builder, ref first, "pgPwrSerdes", JsonUtil.StringValue(snapshot.PgPwrSerdes));
            JsonUtil.Property(builder, ref first, "pgSocVrs11", JsonUtil.StringValue(snapshot.PgSocVrs11));
            JsonUtil.Property(builder, ref first, "socPgLcvr", JsonUtil.StringValue(snapshot.SocPgLcvr));
            JsonUtil.Property(builder, ref first, "socPgLcvrAo", JsonUtil.StringValue(snapshot.SocPgLcvrAo));
        }
    }
}
