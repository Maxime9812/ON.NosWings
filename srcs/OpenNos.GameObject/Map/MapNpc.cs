﻿/*
 * This file is part of the OpenNos Emulator Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using NosSharp.Enums;
using OpenNos.Core;
using OpenNos.Core.Extensions;
using OpenNos.Data;
using OpenNos.GameObject.Event;
using OpenNos.GameObject.Helpers;
using OpenNos.GameObject.Networking;
using OpenNos.GameObject.Npc;
using OpenNos.GameObject.Packets.ServerPackets;
using OpenNos.PathFinder.PathFinder;
using OpenNos.GameObject.Battle;
using static NosSharp.Enums.BCardType;

namespace OpenNos.GameObject.Map
{
    public class MapNpc : MapNpcDTO, IBattleEntity
    {
        #region Members

        public NpcMonster Npc;
        private int _movetime;
        private Random _random;
        public BattleEntity _battleEntity;
        private const int _maxDistance = 20;

        #endregion

        #region Properties

        #region BattleEntityProperties

        public void AddBuff(Buff.Buff indicator) => GetBattleEntity().AddBuff(indicator);

        public void RemoveBuff(short cardId) => GetBattleEntity().RemoveBuff(cardId);

        public int[] GetBuff(CardType type, byte subtype) => GetBattleEntity().GetBuff(type, subtype);

        public bool HasBuff(CardType type, byte subtype) => GetBattleEntity().HasBuff(type, subtype);

        public ConcurrentBag<Buff.Buff> Buffs => GetBattleEntity().Buffs;

        #endregion

        public bool ShouldRespawn { get; set; }

        public bool EffectActivated { get; set; }

        public short FirstX { get; set; }

        public short FirstY { get; set; }

        public bool IsHostile { get; set; }

        public bool IsMate { get; set; }

        public bool IsProtected { get; set; }

        public DateTime Death { get; set; }

        public DateTime LastEffect { get; private set; }

        public DateTime LastMove { get; private set; }

        public DateTime LastSkill { get; set; }

        public IDisposable LifeEvent { get; set; }

        public MapInstance MapInstance { get; set; }

        public ConcurrentBag<EventContainer> OnDeathEvents { get; set; }

        public List<Node> Path { get; set; }

        public List<Recipe> Recipes { get; set; }

        public Shop Shop { get; set; }

        public IBattleEntity Target { get; set; }

        public List<TeleporterDTO> Teleporters { get; set; }

        public IDisposable Life { get; set; }

        public bool IsOut { get; set; }

        public ConcurrentBag<NpcMonsterSkill> Skills { get; set; } = new ConcurrentBag<NpcMonsterSkill>();

        public int CurrentHp { get; set; }

        public int CurrentMp { get; set; }

        public bool IsAlive { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Follow the Monsters target to it's position.
        /// </summary>
        /// <param name="targetSession">The TargetSession to follow</param>
        private void FollowTarget()
        {
            if (Npc == null || !IsAlive || HasBuff(CardType.Move, (byte)AdditionalTypes.Move.MovementImpossible))
            {
                return;
            }
            if (!Target?.isTargetable(GetSessionType()) ?? true)
            {
                RemoveTarget();
                return;
            }
            if (!Path.Any())
            {
                Path = BestFirstSearch.TracePath(new Node() { X = MapX, Y = MapY }, Target.GetBrushFire(), MapInstance.Map.Grid);
            }
            Move(); // follow the target
        }

        public EffectPacket GenerateEff(int effectid)
        {
            return new EffectPacket
            {
                EffectType = 2,
                CharacterId = MapNpcId,
                Id = effectid
            };
        }

        public string GenerateSay(string message, int type)
        {
            return $"say 2 {MapNpcId} 2 {message}";
        }

        public string GenerateIn()
        {
            NpcMonster npcinfo = ServerManager.Instance.GetNpc(NpcVNum);
            if (npcinfo == null || IsDisabled)
            {
                return string.Empty;
            }
            IsOut = false;
            return $"in 2 {NpcVNum} {MapNpcId} {MapX} {MapY} {Position} 100 100 {Dialog} 0 0 -1 1 {(IsSitting ? 1 : 0)} -1 - 0 -1 0 0 0 0 0 0 0 0";
        }

        public string GenerateOut()
        {
            NpcMonster npcinfo = ServerManager.Instance.GetNpc(NpcVNum);
            if (npcinfo == null || IsDisabled)
            {
                return string.Empty;
            }
            IsOut = true;
            return $"out 2 {MapNpcId}";
        }

        public string GetNpcDialog()
        {
            return $"npc_req 2 {MapNpcId} {Dialog}";
        }

        public void Initialize(MapInstance currentMapInstance)
        {
            MapInstance = currentMapInstance;
            Initialize();
        }

        public override void Initialize()
        {
            _random = new Random(MapNpcId);
            Life = null;
            Npc = ServerManager.Instance.GetNpc(NpcVNum);
            LastEffect = DateTime.Now;
            LastMove = DateTime.Now;
            Death = DateTime.Now;
            Npc.Skills.ForEach(s => Skills.Add(s));
            IsHostile = Npc.IsHostile;
            FirstX = MapX;
            EffectActivated = true;
            ShouldRespawn = true;
            FirstY = MapY;
            EffectDelay = 4000;
            _movetime = ServerManager.Instance.RandomNumber(500, 3000);
            Path = new List<Node>();
            Recipes = ServerManager.Instance.GetReceipesByMapNpcId(MapNpcId);
            Target = null;
            Teleporters = ServerManager.Instance.GetTeleportersByNpcVNum((short)MapNpcId);
            Shop shop = ServerManager.Instance.GetShopByMapNpcId(MapNpcId);
            if (shop == null)
            {
                return;
            }
            shop.Initialize();
            Shop = shop;
        }

        public void RunDeathEvent()
        {
            MapInstance.InstanceBag.NpcsKilled++;
            OnDeathEvents.ToList().ForEach(e =>
            {
                if (e.EventActionType == EventActionType.THROWITEMS)
                {
                    Tuple<int, short, byte, int, int> evt = (Tuple<int, short, byte, int, int>)e.Parameter;
                    e.Parameter = new Tuple<int, short, byte, int, int>(MapNpcId, evt.Item2, evt.Item3, evt.Item4, evt.Item5);
                }
                EventHelper.Instance.RunEvent(e);
            });
            OnDeathEvents.Clear();
        }

        public void StartLife()
        {
            if (MapInstance?.IsSleeping == false && Life == null)
            {
                Life = Observable.Interval(TimeSpan.FromMilliseconds(400)).Subscribe(x =>
                {
                    try
                    {
                        if (MapInstance?.IsSleeping == false)
                        {
                            NpcLife();
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                });
            }
        }

        public void StopLife()
        {
            Life?.Dispose();
            Life = null;
        }

        private string GenerateMv2()
        {
            return $"mv 2 {MapNpcId} {MapX} {MapY} {Npc.Speed}";
        }

        public void ShowEffect()
        {
            if (!((DateTime.Now - LastEffect).TotalSeconds >= EffectDelay))
            {
                return;
            }
            if (IsMate || IsProtected)
            {
                MapInstance.Broadcast(GenerateEff(825), MapX, MapY);
            }
            if (Effect > 0 && EffectActivated)
            {
                MapInstance.Broadcast(GenerateEff(Effect), MapX, MapY);
            }
            LastEffect = DateTime.Now;
        }

        private void NpcLife()
        {
            ShowEffect();
            if (!IsHostile)
            {
                Move();
                return;
            }

            if (!IsAlive) // Respawn
            {
                if (!ShouldRespawn)
                {
                    Life.Dispose();
                }
                else if ((DateTime.Now - Death).TotalSeconds >= Npc.RespawnTime / 10d)
                {
                    Respawn();
                }
            }

            if (Target == null) // basic move
            {
                Move();
                return;
            }
            HostilityTarget(); // Get Target if isHostile

            lock (Target)
            {
                NpcMonsterSkill npcMonsterSkill = null;
                if (ServerManager.Instance.RandomNumber(0, 10) > 8 && Skills != null)
                {
                    npcMonsterSkill = Skills.Where(s => (DateTime.Now - s.LastSkillUse).TotalMilliseconds >= 100 * s.Skill.Cooldown).OrderBy(s => _random.Next()).FirstOrDefault();
                }

                if (npcMonsterSkill?.Skill.TargetType == 1 && npcMonsterSkill?.Skill.HitType == 0)
                {
                    TargetHit(npcMonsterSkill);
                }

                // check if target is in range & if monster has enough mp to use the skill
                if (CurrentMp >= (npcMonsterSkill?.Skill.MpCost ?? CurrentMp) && Map.GetDistance(GetPos(), Target.GetPos()) <= (npcMonsterSkill?.Skill.Range + 1 ?? Npc.BasicRange))
                {
                    TargetHit(npcMonsterSkill);
                    return;
                }
                FollowTarget();
            }
        }


        private void Move()
        {
            if (Npc == null || !IsAlive || HasBuff(CardType.Move, (byte)AdditionalTypes.Move.MovementImpossible) || !IsMoving || Npc.Speed <= 0)
            {
                return;
            }

            if (!Path.Any() && (DateTime.Now - LastMove).TotalMilliseconds > _movetime && Target == null) // Basic Move
            {
                short mapX = FirstX, mapY = FirstY;
                if (MapInstance.Map?.GetFreePosition(ref mapX, ref mapY, (byte)ServerManager.Instance.RandomNumber(0, 2), (byte)_random.Next(0, 2)) ?? false)
                {
                    int distance = Map.GetDistance(new MapCell { X = mapX, Y = mapY }, GetPos());
                    double value = 1000d * distance / (2 * Npc.Speed);
                    Observable.Timer(TimeSpan.FromMilliseconds(value)).Subscribe(x =>
                    {
                        MapX = mapX;
                        MapY = mapY;
                    });
                    LastMove = DateTime.Now.AddMilliseconds(value);
                    MapInstance.Broadcast(new BroadcastPacket(null, GenerateMv2(), ReceiverType.All));
                }
            }
            else if (DateTime.Now > LastMove && Path.Any()) // Follow target || move back to original pos
            {
                int maxindex = Path.Count > Npc.Speed / 2 ? Npc.Speed / 2 : Path.Count;
                short smapX = Path[maxindex - 1].X;
                short smapY = Path[maxindex - 1].Y;
                double waitingtime = Map.GetDistance(new MapCell { X = smapX, Y = smapY }, GetPos()) / (double)Npc.Speed;
                MapInstance.Broadcast(new BroadcastPacket(null, $"mv 2 {MapNpcId} {smapX} {smapY} {Npc.Speed}", ReceiverType.All, xCoordinate: smapX, yCoordinate: smapY));
                LastMove = DateTime.Now.AddSeconds(waitingtime > 1 ? 1 : waitingtime);
                Observable.Timer(TimeSpan.FromMilliseconds((int)((waitingtime > 1 ? 1 : waitingtime) * 1000))).Subscribe(x =>
                {
                    MapX = smapX;
                    MapY = smapY;
                });
                if (Target != null && Map.GetDistance(new MapCell { X = FirstX, Y = FirstY }, GetPos()) > _maxDistance) // Remove Target if distance between target & monster is > max Distance
                {
                    RemoveTarget();
                    return;
                }
                Path.RemoveRange(0, maxindex);
            }
            HostilityTarget();
        }

        internal void HostilityTarget()
        {
            if (Target != null)
            {
                return;
            }
            IBattleEntity target = MapInstance.BattleEntities.FirstOrDefault(e => e.isTargetable(GetSessionType()) && Map.GetDistance(GetPos(), e.GetPos()) < Npc.NoticeRange);

            if (target == null)
            {
                return;
            }
            Target = target;
        }

        /// <summary>
        /// Remove the current Target from Npc.
        /// </summary>
        internal void RemoveTarget()
        {
            Target = null;
            Path = BestFirstSearch.FindPath(new Node { X = MapX, Y = MapY }, new Node { X = FirstX, Y = FirstY }, MapInstance.Map.Grid); // Path To origins
        }

        /// <summary>
        /// Start the Respawn
        /// </summary>
        private void Respawn()
        {
            if (Npc == null)
            {
                return;
            }
            IsAlive = true;
            Target = null;
            CurrentHp = Npc.MaxHP;
            CurrentMp = Npc.MaxMP;
            MapX = FirstX;
            MapY = FirstY;
            Path = new List<Node>();
            MapInstance.Broadcast(GenerateIn());
            Npc.BCards.ForEach(s => s.ApplyBCards(this));
        }

        public void TargetHit(NpcMonsterSkill npcMonsterSkill)
        {
            if (Npc == null || (!((DateTime.Now - LastSkill).TotalMilliseconds >= 1000 + Npc.BasicCooldown * 250) && npcMonsterSkill == null) || HasBuff(CardType.SpecialAttack, (byte)AdditionalTypes.SpecialAttack.NoAttack))
            {
                return;
            }

            LastSkill = DateTime.Now;
            if (npcMonsterSkill != null)
            {
                if (CurrentMp < npcMonsterSkill.Skill.MpCost)
                {
                    FollowTarget();
                    return;
                }
                npcMonsterSkill.LastSkillUse = DateTime.Now;
                CurrentMp -= npcMonsterSkill.Skill.MpCost;
                MapInstance.Broadcast($"ct 2 {MapNpcId} {(byte)Target.GetSessionType()} {Target.GetId()} {npcMonsterSkill.Skill.CastAnimation} {npcMonsterSkill.Skill.CastEffect} {npcMonsterSkill.Skill.SkillVNum}");
            }
            LastMove = DateTime.Now;
            GetBattleEntity().TargetHit(Target, TargetHitType.SingleTargetHit, npcMonsterSkill?.Skill, skillEffect: Npc.BasicSkill);
        }

        public MapCell GetPos()
        {
            return new MapCell { X = MapX, Y = MapY };
        }

        public BattleEntity GetBattleEntity()
        {
            return _battleEntity == null ? _battleEntity = new BattleEntity(this) : _battleEntity;
        }

        public object GetSession()
        {
            return this;
        }

        public AttackType GetAttackType(Skill skill = null)
        {
            return (AttackType)Npc.AttackClass;
        }

        public bool isTargetable(SessionType type, bool isPvP = false)
        {
            return type == SessionType.Monster && IsHostile && IsAlive && CurrentHp > 0;
        }

        public Node[,] GetBrushFire()
        {
            return BestFirstSearch.LoadBrushFire(new GridPos() { X = MapX, Y = MapY }, MapInstance.Map.Grid);
        }

        public SessionType GetSessionType()
        {
            return SessionType.MateAndNpc;
        }

        public long GetId()
        {
            return MapNpcId;
        }

        public MapInstance GetMapInstance()
        {
            return MapInstance;
        }

        public void GenerateDeath(IBattleEntity killer)
        {
            if (CurrentHp > 0)
            {
                return;
            }
            IsAlive = false;
            CurrentHp = 0;
            CurrentMp = 0;
            Death = DateTime.Now;
            LastMove = DateTime.Now.AddMilliseconds(500);
            GetBattleEntity().Buffs.Clear();
            Target = null;
            killer?.GenerateRewards(this);
        }

        public void GenerateRewards(IBattleEntity target)
        {

        }

        public int GetCurrentHp()
        {
            return CurrentHp;
        }

        public int GetMaxHp()
        {
            return Npc.MaxHP;
        }

        public void GetDamage(int damage, bool canKill = true)
        {
            CurrentHp -= damage;
            CurrentHp = CurrentHp <= 0 ? !canKill ? 1 : 0 : CurrentHp;
        }

        #endregion
    }
}