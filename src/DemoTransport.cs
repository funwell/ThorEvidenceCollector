using System;
using System.Collections.Generic;
using System.Text;

namespace ThorEvidence.Core
{
    public sealed class DemoTransport
    {
        private readonly Queue<byte[]> _chunks = new Queue<byte[]>();
        private readonly UTF8Encoding _utf8 = new UTF8Encoding(false);

        public bool HasData { get { return _chunks.Count > 0; } }

        public void EnqueueInitial()
        {
            EnqueueText("[demo] NvShell Initialization Start\r\n[demo] Press 'Enter' for NvShell prompt\r\nNvShell>\r\n");
        }

        public bool Write(byte[] bytes)
        {
            string text = _utf8.GetString(bytes ?? new byte[0]);
            string command = text.Trim('\r', '\n', ' ', '\t');
            if (command.Length == 0)
            {
                EnqueueText("NvShell>\r\n");
                return true;
            }

            if (!CommandPolicy.IsAllowed(command))
            {
                EnqueueText("NvShell>\r\n");
                return true;
            }

            StringBuilder response = new StringBuilder();
            response.Append(command);
            response.Append("\r\n");
            if (command == "version")
            {
                response.Append("[mcu_version:ZRD.MCU.DEMO.V1.0]\r\n");
                response.Append("mcuA Version 1.0.3\r\n");
                response.Append("[build_date:demo]\r\n");
            }
            else if (command == "help")
            {
                response.Append("Available commands (read-only demo subset):\r\n");
                response.Append("version\r\nhelp\r\nshowvoltages\r\npmstateget\r\nreadtemp\r\n");
            }
            else if (command == "showvoltages")
            {
                response.Append("SENSE_KL30_VBAT = 12.100 V\r\n");
                response.Append("SENSE_VDD_12V = 12.100 V\r\n");
                response.Append("SENSE_ACC1 = 0.000 V\r\n");
                response.Append("SENSE_SERDES_1V8 = 0.000 V\r\n");
                response.Append("SENSE_SERDES_1V2 = 0.000 V\r\n");
                response.Append("SENSE_SERDES_1V0 = 0.000 V\r\n");
                response.Append("SENSE_SOC_PPVCC_UFS = 0.000 V\r\n");
                response.Append("PG_SOC_VRS11 = 0.000 V\r\n");
            }
            else if (command == "pmstateget")
            {
                response.Append("PM_StateM: INIT->STANDBY\r\n");
                response.Append("PM_State 0\r\n");
                response.Append("Cur wake-up src:0\r\n");
            }
            else if (command == "readtemp")
            {
                response.Append("mcu_temp: 31.0 C\r\n");
                response.Append("T_Soc: 0.0 C, soc get temp fail\r\n");
                response.Append("MCU_PLTFPWRMGR_REQ_POWERDOWN\r\n");
                response.Append("PM_StateM: STANDBY->SLEEP\r\n");
            }
            response.Append("NvShell>\r\n");
            EnqueueText(response.ToString());
            return true;
        }

        public byte[] ReadChunk()
        {
            return _chunks.Count == 0 ? null : _chunks.Dequeue();
        }

        private void EnqueueText(string text)
        {
            byte[] bytes = _utf8.GetBytes(text ?? "");
            int offset = 0;
            while (offset < bytes.Length)
            {
                int length = Math.Min(17, bytes.Length - offset);
                byte[] chunk = new byte[length];
                Buffer.BlockCopy(bytes, offset, chunk, 0, length);
                _chunks.Enqueue(chunk);
                offset += length;
            }
        }
    }
}
