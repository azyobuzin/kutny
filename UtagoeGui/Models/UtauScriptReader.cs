using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UtagoeGui.Models
{
    /// <summary>
    /// UTAU スクリプトファイル(.ust)を解析します。
    /// </summary>
    public class UtauScriptReader : IDisposable
    {
        private static readonly Encoding s_encoding = Encoding.GetEncoding(932); // Shift_JIS
        private readonly StreamReader _reader;
        private readonly Dictionary<string, string> _fields;
        private string _nextLine;

        public UtauScriptReader(Stream stream, bool leaveOpen = false)
        {
            this._reader = new StreamReader(stream, s_encoding, false, 1024, leaveOpen);
        }

        public string SectionName { get; private set; }

        public string GetField(string fieldName)
        {
            this._fields.TryGetValue(fieldName, out var x);
            return x;
        }

        public bool ReadSection()
        {
            if (string.IsNullOrEmpty(this._nextLine) || this._nextLine[0] != '[')
                return false;

            this.SectionName = this._nextLine.Substring(1, this._nextLine.Length - 2);
            this._fields.Clear();

            while (true)
            {
                this._nextLine = this._reader.ReadLine();

                if (string.IsNullOrEmpty(this._nextLine) || this._nextLine[0] == '[')
                    return true;

                var eqIndex = this._nextLine.IndexOf('=');
                var key = eqIndex >= 0 ? this._nextLine.Remove(eqIndex) : this._nextLine;
                var value = eqIndex >= 0 ? this._nextLine.Substring(eqIndex + 1) : "";
                this._fields.Add(key, value);
            }
        }

        public void Dispose()
        {
            this._reader.Dispose();
        }
    }
}
