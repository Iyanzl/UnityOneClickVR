#if OPENXR_BUILD
using System;
using System.IO;
using System.Reflection;
using HybridVR.Shared;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr
{
    // Optional dependency loaded on demand. A missing driver must not prevent VR startup.
    internal sealed class VirtualXboxOutput : IDisposable
    {
        private object _client, _controller;
        private MethodInfo _buttons, _axis, _slider, _submit, _reset, _disconnect;
        private object[] _axes, _sliders;
        private bool _neutral = true;
        internal string Status { get; private set; } = "Xbox: not connected";
        internal bool Connected => _controller != null;

        internal bool Connect()
        {
            if (Connected) return true;
            try
            {
                string directory = Path.GetDirectoryName(typeof(VirtualXboxOutput).Assembly.Location);
                var assembly = Assembly.LoadFrom(Path.Combine(directory, "Nefarius.ViGEm.Client.dll"));
                Type clientType = assembly.GetType("Nefarius.ViGEm.Client.ViGEmClient", true);
                _client = Activator.CreateInstance(clientType);
                // Use the actual interface to handle explicitly implemented members.
                _controller = clientType.GetMethod("CreateXbox360Controller", Type.EmptyTypes).Invoke(_client, null);
                Type iface = assembly.GetType("Nefarius.ViGEm.Client.Targets.IXbox360Controller", true);
                Type common = assembly.GetType("Nefarius.ViGEm.Client.IVirtualGamepad", true);
                PropertyInfo autoSubmit = iface.GetProperty("AutoSubmitReport") ?? common.GetProperty("AutoSubmitReport");
                autoSubmit.SetValue(_controller, false, null);
                MethodInfo connect = iface.GetMethod("Connect") ?? common.GetMethod("Connect");
                _disconnect = iface.GetMethod("Disconnect") ?? common.GetMethod("Disconnect");
                _buttons = iface.GetMethod("SetButtonsFull");
                _axis = iface.GetMethod("SetAxisValue"); _slider = iface.GetMethod("SetSliderValue");
                _submit = iface.GetMethod("SubmitReport") ?? common.GetMethod("SubmitReport");
                _reset = iface.GetMethod("ResetReport") ?? common.GetMethod("ResetReport");
                Type axisType = assembly.GetType("Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis", true);
                Type sliderType = assembly.GetType("Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Slider", true);
                _axes = new[] { Constant(axisType,"LeftThumbX"), Constant(axisType,"LeftThumbY"), Constant(axisType,"RightThumbX"), Constant(axisType,"RightThumbY") };
                _sliders = new[] { Constant(sliderType,"LeftTrigger"), Constant(sliderType,"RightTrigger") };
                if (_buttons == null || _axis == null || _slider == null || _submit == null || _reset == null || _disconnect == null)
                    throw new MissingMethodException("ViGEm interface does not match the packaged client");
                connect.Invoke(_controller, null);
                _neutral = false; Neutral();
                Status = "Xbox: connected";
                OpenVrDiagnostics.Write(Status);
                return true;
            }
            catch (Exception error)
            {
                Dispose();
                Status = "Xbox unavailable: " + (error.InnerException ?? error).Message;
                OpenVrDiagnostics.Write(Status);
                return false;
            }
        }

        private static object Constant(Type type, string name)
        {
            if (type.IsEnum) return Enum.Parse(type, name);
            return type.GetField(name, BindingFlags.Public|BindingFlags.Static)?.GetValue(null) ??
                type.GetProperty(name, BindingFlags.Public|BindingFlags.Static)?.GetValue(null, null) ??
                throw new MissingFieldException(type.FullName, name);
        }

        internal void Update(bool enabled, ControllerSample s, float leftTrigger, float rightTrigger)
        {
            if (!Connected) return;
            try
            {
                if (!enabled) { Neutral(); return; }
                ushort buttons = (ushort)((s.A?0x1000:0)|(s.B?0x2000:0)|(s.X?0x4000:0)|(s.Y?0x8000:0)|
                    (s.L3?0x40:0)|(s.R3?0x80:0)|(s.LeftGrip?0x100:0)|(s.RightGrip?0x200:0));
                _buttons.Invoke(_controller, new object[]{buttons});
                double[] values = {s.LeftStickX,s.LeftStickY,s.RightStickX,s.RightStickY};
                for(int i=0;i<4;i++) _axis.Invoke(_controller,new object[]{_axes[i],Axis(values[i])});
                _slider.Invoke(_controller,new object[]{_sliders[0],Trigger(leftTrigger)});
                _slider.Invoke(_controller,new object[]{_sliders[1],Trigger(rightTrigger)});
                _submit.Invoke(_controller,null); _neutral=false;
            }
            catch (Exception error)
            {
                Dispose(); Status="Xbox output failed: "+(error.InnerException??error).Message;
                OpenVrDiagnostics.Write(Status);
            }
        }
        private static short Axis(double v) => (short)Math.Round(Math.Max(-1,Math.Min(1,v))*32767);
        private static byte Trigger(float v) => (byte)Math.Round(Math.Max(0,Math.Min(1,v))*255);
        internal void Neutral()
        {
            if (_controller==null || _neutral) return;
            _reset?.Invoke(_controller,null); _submit?.Invoke(_controller,null); _neutral=true;
        }
        public void Dispose()
        {
            try { Neutral(); } catch { }
            try { _disconnect?.Invoke(_controller,null); } catch { }
            try { (_controller as IDisposable)?.Dispose(); } catch { }
            try { (_client as IDisposable)?.Dispose(); } catch { }
            _controller=_client=null; _neutral=true;
        }
    }
}
#endif
