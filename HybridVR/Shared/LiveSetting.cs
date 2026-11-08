#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HybridVR.Shared
{
    public enum SettingChangeKind { Configured, Unchanged, Unsupported, Rejected, Failed }

    public sealed class SettingChangeResult
    {
        public string Id = "";
        public string? Requested, Readback, Error;
        public SettingChangeKind Kind;
        // Configuration readback is not proof that a rendering backend applied it.
        public bool RuntimeConfirmed => false;
    }

    public sealed class LiveSetting
    {
        public string Id { get; private set; }
        public string Unit { get; private set; }
        public string Group = "", Zh = "", En = "";
        public bool Numeric { get; private set; }
        public Func<string> Value { get; private set; }
        public Action<int>? Change { get; private set; }
        public SettingChangeResult? LastResult { get; private set; }
        private Func<string, string>? _normalize;
        private Action<string>? _write;
        private Func<int, string>? _step;

        private LiveSetting(string id, string unit, Func<string> read)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Setting ID required");
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '.' && c != '-')
                    throw new ArgumentException("Setting ID must be stable lower-case ASCII: " + id);
            Id = id; Unit = unit ?? ""; Value = read ?? throw new ArgumentNullException(nameof(read));
        }

        public string Label(bool chinese) => (chinese ? Zh : En) + ": " + Value();

        public static LiveSetting ReadOnly(string id, Func<string> read) => new LiveSetting(id, "", read);

        public static LiveSetting Number(string id, string unit, Func<float> read, Action<float> write,
            float step, float minimum, float maximum)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (write == null) throw new ArgumentNullException(nameof(write));
            if (!Finite(step) || step <= 0 || !Finite(minimum) || !Finite(maximum) || minimum > maximum)
                throw new ArgumentException("Invalid number bounds");
            var setting = new LiveSetting(id, unit, () => read().ToString("0.###", CultureInfo.InvariantCulture)) { Numeric = true };
            setting._normalize = text => {
                float value;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                    !Finite(value) || value < minimum || value > maximum) throw new ArgumentException("Number outside supported range");
                return value.ToString("R", CultureInfo.InvariantCulture);
            };
            setting._write = text => write(float.Parse(text, CultureInfo.InvariantCulture));
            // Use round-trip values for validation, not rounded display text.
            setting._readRaw = () => read().ToString("R", CultureInfo.InvariantCulture);
            setting._step = direction => {
                float current = read();
                if (!Finite(current)) throw new ArgumentException("Current value is not finite");
                return Math.Max(minimum, Math.Min(maximum, current + Math.Sign(direction) * step)).ToString("R", CultureInfo.InvariantCulture);
            };
            setting.EnableChanges(); return setting;
        }

        public static LiveSetting Toggle(string id, Func<bool> read, Action<bool> write)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (write == null) throw new ArgumentNullException(nameof(write));
            var setting = new LiveSetting(id, "boolean", () => read() ? "[x]" : "[ ]");
            setting._readRaw = () => read() ? "true" : "false";
            setting._normalize = text => {
                bool value;
                if (!bool.TryParse(text, out value)) throw new ArgumentException("Boolean required");
                return value ? "true" : "false";
            };
            setting._write = text => write(text == "true");
            setting._step = direction => read() ? "false" : "true";
            setting.EnableChanges(); return setting;
        }

        public static LiveSetting Choice(string id, Func<string> read, Action<string> write, string[] options)
        {
            if (options == null || options.Length == 0) throw new ArgumentException("Choices required");
            var choices = (string[])options.Clone();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in choices) if (string.IsNullOrEmpty(value) || !seen.Add(value)) throw new ArgumentException("Invalid or duplicate choice");
            var setting = new LiveSetting(id, "choice", read);
            setting._normalize = text => {
                if (!seen.Contains(text)) throw new ArgumentException("Unknown choice");
                return text;
            };
            setting._write = write ?? throw new ArgumentNullException(nameof(write));
            setting._step = direction => {
                int index = Array.IndexOf(choices, read());
                if (index < 0) throw new ArgumentException("Current value is not in the supported choices");
                return choices[(index + Math.Sign(direction) + choices.Length) % choices.Length];
            };
            setting.EnableChanges(); return setting;
        }

        private Func<string>? _readRaw;
        private void EnableChanges() { Change = direction => Adjust(direction); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public SettingChangeResult Adjust(int direction)
        {
            if (_step == null) return Complete(SettingChangeKind.Unsupported, null, null, "Read-only setting");
            try {
                if (direction == 0) return Complete(SettingChangeKind.Unchanged, null, (_readRaw ?? Value)(), null);
                return Configure(_step(direction));
            }
            catch (Exception error) { return Complete(SettingChangeKind.Failed, null, null, error.Message); }
        }

        public SettingChangeResult Configure(string requested)
        {
            if (_write == null || _normalize == null) return Complete(SettingChangeKind.Unsupported, requested, null, "Read-only setting");
            string value;
            try { value = _normalize(requested); }
            catch (Exception error) { return Complete(SettingChangeKind.Rejected, requested, null, error.Message); }
            try {
                string previous = (_readRaw ?? Value)();
                if (previous == value) return Complete(SettingChangeKind.Unchanged, value, previous, null);
                _write(value);
                string readback = (_readRaw ?? Value)();
                if (readback != value) return Complete(SettingChangeKind.Failed, value, readback, "Configuration readback mismatch");
                return Complete(SettingChangeKind.Configured, value, readback, null);
            }
            catch (Exception error) { return Complete(SettingChangeKind.Failed, value, null, error.Message); }
        }

        private SettingChangeResult Complete(SettingChangeKind kind, string? requested, string? readback, string? error)
        {
            LastResult = new SettingChangeResult { Id = Id, Kind = kind, Requested = requested, Readback = readback, Error = error };
            return LastResult;
        }
    }
}
