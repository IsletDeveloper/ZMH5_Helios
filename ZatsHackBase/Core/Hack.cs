﻿using ZatsHackBase.Core.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZatsHackBase.UI;
using System.Threading;
using ZatsHackBase.Maths;
using ZatsHackBase.Drawing;
using ZatsHackBase.Input;

namespace ZatsHackBase.Core
{
    public abstract class Hack
    {
        #region VARIABLES
        private LoopTicker ticker;
        private List<HackModule> modules;
        private bool first = true;
        #endregion

        #region PROPERTIES
        public EUCProcess Process { get; private set; }
        public Memory Memory { get { return Process.Memory; } }
        public HackOverlay Overlay { get; private set; }
        public HackInput Input { get; private set; }
        public double TicksPerSecond { get { return ticker.TicksPerSeconds; } }
        #endregion

        #region CONSTRUCTORS
        public Hack(string processName, int tickRate = 60, bool createOverlay = true, bool limitFrames = true, int timeOut = -1)
        {
            Input = new HackInput();
            Process = EUCProcess.WaitForProcess(processName, timeOut);
            ticker = new LoopTicker(tickRate, limitFrames);
            modules = new List<HackModule>();
            if (createOverlay)
            {
                Overlay = new HackOverlay(Process, Input);
                Overlay.Start();
            }

            ticker.Tick += (o, e) =>
            {
                if (!Process.IsRunning)
                {
                    e.Stop = true;
                    return;
                }

                OnTick(e);
            };
            ticker.AfterRun += (o, e) => AfterRun();
            ticker.BeforeRun += (o, e) => BeforeRun();
        }
        #endregion

        #region METHODS
        protected void AddModule(HackModule mod)
        {
            modules.Add(mod);
        }
        protected void RemoveModule(HackModule mod)
        {
            modules.Remove(mod);
        }
        public void Run()
        {
            ticker.Run();
        }

        protected virtual void OnFirstTick(TickEventArgs args)
        {
            first = false;
            SetupModules();
        }
        protected abstract void SetupModules();
        protected virtual void AfterRun() { }
        protected virtual void BeforeRun() { }
        protected virtual void OnTick(TickEventArgs args)
        {
            Process.Process.Refresh();
            if (!Process.IsRunning)
            {
                args.Stop = true;
                return;
            }


            if (first)
                OnFirstTick(args);

            if (ProcessInput())
                Input.Update();

            BeforePluginsTick(args);

            if (ProcessModules())
            {
                foreach (var mod in modules.OrderByDescending(x => x.Priority))
                    mod.Update(args);
            }

            AfterPluginsTick(args);
        }
        protected virtual bool ProcessModules()
        {
            return Process.IsInForeground;
        }
        protected virtual bool ProcessInput()
        {
            return true;
        }
        protected virtual void BeforePluginsTick(TickEventArgs args)
        {
            if (Overlay != null && Overlay.Form != null)
            {
                Overlay.Update(args.Time, Input.MousePos - new Vector2(Overlay.Form.Location.X, Overlay.Form.Location.Y));
                Overlay.Renderer.Clear(Overlay.BackColor);
            }
        }
        protected virtual void AfterPluginsTick(TickEventArgs args)
        {
            if (Overlay != null)
                Overlay.Renderer.Present();
        }
        #endregion
    }
}
