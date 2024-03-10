﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Blish_HUD.ArcDps;
using Blish_HUD.ArcDps.Common;
using Blish_HUD.ArcDps.Models;
using Blish_HUD.GameServices.ArcDps.V2.Models;
using Microsoft.Xna.Framework;

namespace Blish_HUD {

    public class ArcDpsService : GameService {
        private static readonly Logger Logger = Logger.GetLogger<ArcDpsService>();
        private static readonly object WatchLock = new object();

        private readonly ConcurrentDictionary<uint, ConcurrentBag<Action<object, RawCombatEventArgs>>> _subscriptions =
            new ConcurrentDictionary<uint, ConcurrentBag<Action<object, RawCombatEventArgs>>>();

#if DEBUG
        public static long Counter;
        #endif

        /// <summary>
        ///     Triggered upon error of the underlaying socket listener.
        /// </summary>
        public event EventHandler<SocketError> Error;

        /// <summary>
        ///     Provides common fields that multiple modules might want to track
        /// </summary>
        public CommonFields Common { get; private set; }

        /// <summary>
        ///     Indicates if arcdps updated <see cref="HudIsActive" /> in the last second (it should every in-game frame)
        /// </summary>
        public bool RenderPresent => GameService.ArcDpsV2.RenderPresent;

        /// <summary>
        ///     Indicates if the socket listener for the arcdps service is running and arcdps sent an update in the last second.
        /// </summary>
        public bool Running => GameService.ArcDpsV2.Running;

        /// <summary>
        ///     Indicates if arcdps currently draws its HUD (not in character select, cut scenes or loading screens)
        /// </summary>
        public bool HudIsActive => GameService.ArcDpsV2.HudIsActive;
        

        /// <summary>
        /// The timespan after which ArcDPS is treated as not responding.
        /// </summary>
        private readonly TimeSpan _leeway = TimeSpan.FromMilliseconds(1000);

        private Stopwatch _stopwatch;
        private bool _subscribed;

        public void SubscribeToCombatEventId(Action<object, RawCombatEventArgs> func, params uint[] skillIds) {

            if (!_subscribed) {
                GameService.ArcDpsV2.RegisterMessageType<CombatCallback>(0, async (combatEvent, ct) => {
                    DispatchSkillSubscriptions(combatEvent);
                    await System.Threading.Tasks.Task.CompletedTask;
                });
                _subscribed         =  true;
            }

            foreach (uint skillId in skillIds) {
                if (!_subscriptions.ContainsKey(skillId)) _subscriptions.TryAdd(skillId, new ConcurrentBag<Action<object, RawCombatEventArgs>>());

                _subscriptions[skillId].Add(func);
            }
        }

        private void DispatchSkillSubscriptions(CombatCallback combatEvent) {
            uint skillId = combatEvent.Event.SkillId;
            if (!_subscriptions.ContainsKey(skillId)) return;

            foreach (Action<object, RawCombatEventArgs> action in _subscriptions[skillId]) {
                action(this, new RawCombatEventArgs(
                    new ArcDps.Models.CombatEvent(
                        new Ev(
                            combatEvent.Event.Time,
                            combatEvent.Event.SourceAgent,
                            combatEvent.Event.DestinationAgent,
                            combatEvent.Event.Value,
                            combatEvent.Event.BuffDamage,
                            combatEvent.Event.OverstackValue,
                            combatEvent.Event.SkillId,
                            combatEvent.Event.SourceInstanceId,
                            combatEvent.Event.DestinationInstanceId,
                            combatEvent.Event.SourceMasterInstanceId,
                            combatEvent.Event.DestinationMasterInstanceId,
                            (ArcDpsEnums.IFF)(int)combatEvent.Event.Iff,
                            combatEvent.Event.Buff,
                            combatEvent.Event.Result,
                            (ArcDpsEnums.Activation)(int)combatEvent.Event.IsActivation,
                            (ArcDpsEnums.BuffRemove)(int)combatEvent.Event.IsBuffRemoved,
                            combatEvent.Event.IsNinety,
                            combatEvent.Event.IsFifty,
                            combatEvent.Event.IsMoving,
                            (ArcDpsEnums.StateChange)(int)combatEvent.Event.IsStateChanged,
                            combatEvent.Event.IsFlanking,
                            combatEvent.Event.IsShiels,
                            combatEvent.Event.IsOffCycle,
                            combatEvent.Event.Pad61,
                            combatEvent.Event.Pad62,
                            combatEvent.Event.Pad63,
                            combatEvent.Event.Pad64),
                        new Ag(
                            combatEvent.Source.Name, 
                            combatEvent.Source.Id, 
                            combatEvent.Source.Profession, 
                            combatEvent.Source.Elite, 
                            combatEvent.Source.Self, 
                            combatEvent.Source.Team),
                        new Ag(
                            combatEvent.Destination.Name, 
                            combatEvent.Destination.Id, 
                            combatEvent.Destination.Profession, 
                            combatEvent.Destination.Elite, 
                            combatEvent.Destination.Self, 
                            combatEvent.Destination.Team),
                        combatEvent.SkillName,
                        combatEvent.Id,
                        combatEvent.Revision),
                    RawCombatEventArgs.CombatEventType.Local));
            }
        }

        /// <remarks>
        ///     Please note that you block the socket server with whatever
        ///     you are doing on this event. So please don't do anything
        ///     that requires heavy work. Make your own worker thread
        ///     if you need to.
        ///     Also note, that this is not the main thread, so operations
        ///     other parts of BHUD have to be thread safe.
        /// </remarks>
        /// <summary>
        ///     Holds unprocessed combat data
        /// </summary>
        public event EventHandler<RawCombatEventArgs> RawCombatEvent;

        protected override void Initialize() {
            GameService.ArcDpsV2.Error += Error;

            this.Common             =  new CommonFields();
            _stopwatch              =  new Stopwatch();
            #if DEBUG
            this.RawCombatEvent += (a, b) => { Interlocked.Increment(ref Counter); };
            #endif
        }

        protected override void Load() {
            _stopwatch.Start();
        }

        protected override void Unload() {
            _stopwatch.Stop();
        }

        protected override void Update(GameTime gameTime) {
            TimeSpan elapsed;

            lock (WatchLock) {
                elapsed = _stopwatch.Elapsed;
            }
        }
    }
}