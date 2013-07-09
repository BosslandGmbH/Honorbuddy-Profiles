﻿// Originally contributed by Chinajade.
//
// LICENSE:
// This work is licensed under the
//     Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.
// also known as CC-BY-NC-SA.  To view a copy of this license, visit
//      http://creativecommons.org/licenses/by-nc-sa/3.0/
// or send a letter to
//      Creative Commons // 171 Second Street, Suite 300 // San Francisco, California, 94105, USA.

#region Summary and Documentation
// QUICK DOX:
// TEMPLATE.cs is a skeleton for creating new quest behaviors.
//
// Quest binding:
//      QuestId [REQUIRED if EscortCompleteWhen=QuestComplete; Default:none]:
//      QuestCompleteRequirement [Default:NotComplete]:
//      QuestInLogRequirement [Default:InLog]:
//              A full discussion of how the Quest* attributes operate is described in
//              http://www.thebuddyforum.com/mediawiki/index.php?title=Honorbuddy_Programming_Cookbook:_QuestId_for_Custom_Behaviors
//      QuestObjectiveIndex [REQUIRED if EventCompleteWhen=QuestObjectiveComplete]
//          [on the closed interval: [1..5]]
//          This argument is only consulted if EventCompleteWhen is QuestObjectveComplete.
//          The argument specifies the index of the sub-goal of a quest.
//
// Tunables (ideally, the profile would _never_ provide these arguments):
//      CombatMaxEngagementDistance [optional; Default: 23.0]
//          This is a work around for some buggy Combat Routines.  If a targetted mob is
//          "too far away", some Combat Routines refuse to engage it for killing.  This
//          value moves the toon within an appropriate distance to the requested target
//          so the Combat Routine will perform as expected.
//      IgnoreMobsInBlackspots [optional; Default: true]
//          When true, any mobs within (or too near) a blackspot will be ignored
//          in the list of viable targets that are considered for item use.
//      MovementBy [optional; Default: FlightorPreferred]
//          [allowed values: ClickToMoveOnly/FlightorPreferred/NavigatorOnly/NavigatorPreferred/None]
//          Allows alternative navigation techniques.  You should provide this argument
//          only when an area is unmeshed, or not meshed well.  If ClickToMoveOnly
//          is specified, the area must be free of obstacles; otherwise, the toon
//          will get hung up.
//      NonCompeteDistance [optional; Default: 20]
//          If a player is within this distance of a target that looks
//          interesting to us, we'll ignore the target.  The assumption is that the player may
//          be going for the same target, and we don't want to draw attention.
//          Shared resources, such as Vendors, Innkeepers, Trainers, etc. are never considered
//          to be "in competition".
//
// BEHAVIOR EXTENSION ELEMENTS (goes between <CustomBehavior ...> and </CustomBehavior> tags)
// See the "Examples" section for typical usage.
//      Blackspots [optional; Default: none]
//          Specifies a set of blackspots that will be temporarily installed while the behavior
//          is running.  When the behavior completes, or the Honorbuddy is stopped, these temporary
//          blackspots will be removed.
//          This element expects a list of <Blackspot> sub-elements.
//
//      Blackspot
//          Specifies a single blackspot with the following attributes:
//              Name [optional; Default: X/Y/Z location of the blackspot]
//                  The name of the waypoint is presented to the user as it is visited.
//                  This can be useful for debugging purposes, and for making minor adjustments
//                  (you know which waypoint to be fiddling with).
//              X/Y/Z [REQUIRED; Default: none]
//                  The world coordinates of the blackspot.
//              Radius [optional; Default: 10.0]
//                  The radius of the blackspot.
//              Height [optional; Default 1.0]
//                  The height of the blackspot.
//
// THINGS TO KNOW:
//
// EXAMPLE:
//      <CustomBehavior File="TEMPLATE" >
//          <Blackspots>
//              <Blackspot X="2053.501" Y="-5783.61" Z="101.3919" Radius="15.69214" />
//              <Blackspot X="1639.395" Y="-5847.581" Z="116.1873" Radius="18.69113" />
//              <Blackspot X="1758.225" Y="-5873.512" Z="116.1236" Radius="30.69964" />
//              <Blackspot X="1777.866" Y="-5923.742" Z="116.1065" Radius="5.556122" />
//          </Blackspots>
//      </CustomBehavior>
#endregion

#region Usings
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Bots.Grind;

using Honorbuddy.QuestBehaviorCore.XmlElements;

using Styx;
using Styx.Common;
using Styx.CommonBot;
using Styx.CommonBot.Profiles;
using Styx.Pathing;
using Styx.TreeSharp;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

#endregion


namespace Honorbuddy.QuestBehaviorCore
{
    public abstract partial class QuestBehaviorBase : CustomForcedBehavior
    {
        #region Consructor and Argument Processing
        protected QuestBehaviorBase(Dictionary<string, string> args,
                                    bool isDoneChecksQuestProgress = true)
            : base(args)
        {
            QBCLog.BehaviorLoggingContext = this;
            _isDoneChecksQuestProgress = isDoneChecksQuestProgress;

            try
            {
                // Quest handling...
                // QuestRequirement* attributes are explained here...
                //    http://www.thebuddyforum.com/mediawiki/index.php?title=Honorbuddy_Programming_Cookbook:_QuestId_for_Custom_Behaviors
                // ...and also used for IsDone processing.
                QuestId = GetAttributeAsNullable<int>("QuestId", false, ConstrainAs.QuestId(this), null) ?? 0;
                QuestRequirementComplete = GetAttributeAsNullable<QuestCompleteRequirement>("QuestCompleteRequirement", false, null, null) ?? QuestCompleteRequirement.NotComplete;
                QuestRequirementInLog = GetAttributeAsNullable<QuestInLogRequirement>("QuestInLogRequirement", false, null, null) ?? QuestInLogRequirement.InLog;
                QuestObjectiveIndex = GetAttributeAsNullable<int>("QuestObjectiveIndex", false, new ConstrainTo.Domain<int>(1, 5), null) ?? 0;

                // Tunables...
                CombatMaxEngagementDistance = GetAttributeAsNullable<double>("CombatMaxEngagementDistance", false, new ConstrainTo.Domain<double>(1.0, 40.0), null) ?? 23.0;
                IgnoreMobsInBlackspots = GetAttributeAsNullable<bool>("IgnoreMobsInBlackspots", false, null, null) ?? true;
                MaxDismountHeight = GetAttributeAsNullable<double>("MaxDismountHeight", false, new ConstrainTo.Domain<double>(1.0, 75.0), null) ?? 8.0;
                MovementBy = GetAttributeAsNullable<MovementByType>("MovementBy", false, null, null) ?? MovementByType.FlightorPreferred;
                NonCompeteDistance = GetAttributeAsNullable<double>("NonCompeteDistance", false, new ConstrainTo.Domain<double>(0.0, 50.0), null) ?? 20.0;
            }

            catch (Exception except)
            {
                if (Query.IsExceptionReportingNeeded(except))
                {
                    // Maintenance problems occur for a number of reasons.  The primary two are...
                    // * Changes were made to the behavior, and boundary conditions weren't properly tested.
                    // * The Honorbuddy core was changed, and the behavior wasn't adjusted for the new changes.
                    // In any case, we pinpoint the source of the problem area here, and hopefully it can be quickly
                    // resolved.
                    QBCLog.Error("[MAINTENANCE PROBLEM]: " + except.Message
                             + "\nFROM HERE:\n"
                             + except.StackTrace + "\n");
                }
                IsAttributeProblem = true;
            }
        }


        // Variables for Attributes provided by caller
        public double CombatMaxEngagementDistance { get; private set; }
        public bool IgnoreMobsInBlackspots { get; private set; }
        public double MaxDismountHeight { get; private set; }
        public MovementByType MovementBy { get; set; }
        public int QuestId { get; private set; }
        public int QuestObjectiveIndex { get; private set; }
        public QuestCompleteRequirement QuestRequirementComplete { get; private set; }
        public QuestInLogRequirement QuestRequirementInLog { get; private set; }
        public double NonCompeteDistance { get; private set; }

        // DON'T EDIT THESE--they are auto-populated by Subversion
        public override string SubversionId { get { return "$Id: QuestBehaviorBase.cs 598 2013-07-09 16:08:33Z chinajade $"; } }
        public override string SubversionRevision { get { return "$Rev: 598 $"; } }
        #endregion


        #region Private and Convenience variables
        protected BehaviorFlags _behaviorFlagsOriginal;
        private Composite _behaviorTreeHook_CombatMain;
        private Composite _behaviorTreeHook_CombatOnly;
        private Composite _behaviorTreeHook_DeathMain;
        private Composite _behaviorTreeHook_QuestbotMain;
        private Composite _behaviorTreeHook_Main;
        private ConfigMemento _mementoSettings;
        private bool _isBehaviorDone;
        private bool _isDoneChecksQuestProgress;
        protected bool _isDisposed { get; private set; }
        private BlackspotsType _localBlackspots { get; set; }
        #endregion


        #region Destructor, Dispose, and cleanup
        ~QuestBehaviorBase()
        {
            Dispose(false);
        }


        // 24Feb2013-08:10UTC chinajade
        protected virtual void Dispose(bool isExplicitlyInitiatedDispose)
        {
            if (!_isDisposed)
            {
                BotEvents.OnBotStop -= BotEvents_OnBotStop;

                // NOTE: we should call any Dispose() method for any managed or unmanaged
                // resource, if that resource provides a Dispose() method.

                // Clean up managed resources, if explicit disposal...
                if (isExplicitlyInitiatedDispose)
                {
                    // empty, for now
                }

                // Clean up unmanaged resources (if any) here...

                if (Targeting.Instance != null)
                {
                    Targeting.Instance.IncludeTargetsFilter -= TargetFilter_IncludeTargets;
                    Targeting.Instance.RemoveTargetsFilter -= TargetFilter_RemoveTargets;
                    Targeting.Instance.WeighTargetsFilter -= TargetFilter_WeighTargets;
                }


                // NB: we don't unhook _behaviorTreeHook_Main
                // This was installed when HB created the behavior, and its up to HB to unhook it

                if (_behaviorTreeHook_CombatMain != null)
                {
                    TreeHooks.Instance.RemoveHook("Combat_Main", _behaviorTreeHook_CombatMain);
                    _behaviorTreeHook_CombatMain = null;
                }

                if (_behaviorTreeHook_CombatOnly != null)
                {
                    TreeHooks.Instance.RemoveHook("Combat_Only", _behaviorTreeHook_CombatOnly);
                    _behaviorTreeHook_CombatOnly = null;
                }

                if (_behaviorTreeHook_DeathMain != null)
                {
                    TreeHooks.Instance.RemoveHook("Death_Main", _behaviorTreeHook_DeathMain);
                    _behaviorTreeHook_DeathMain = null;
                }

                if (_behaviorTreeHook_QuestbotMain != null)
                {
                    TreeHooks.Instance.RemoveHook("Questbot_Main", _behaviorTreeHook_QuestbotMain);
                    _behaviorTreeHook_DeathMain = null;
                }

                // Remove 'local' blackspots...
                BlackspotManager.RemoveBlackspots(_localBlackspots.GetBlackspots());

                // Restore configuration...
                if (_mementoSettings != null)
                {
                    _mementoSettings.Dispose();
                    _mementoSettings = null;
                }

                LevelBot.BehaviorFlags = _behaviorFlagsOriginal;

                TreeRoot.GoalText = string.Empty;
                TreeRoot.StatusText = string.Empty;

                QBCLog.BehaviorLoggingContext = null;
                base.Dispose();
            }

            _isDisposed = true;
        }


        private void BotEvents_OnBotStop(EventArgs args)
        {
            Dispose();
        }
        #endregion


        #region Overrides of CustomForcedBehavior

        protected sealed override Composite CreateBehavior()
        {
            return _behaviorTreeHook_Main
                ?? (_behaviorTreeHook_Main = new ExceptionCatchingWrapper(this, CreateMainBehavior()));
        }


        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        public sealed override bool IsDone
        {
            get
            { 
                return _isBehaviorDone     // normal completion
                        || Me.IsQuestObjectiveComplete(QuestId, QuestObjectiveIndex)
                        || (_isDoneChecksQuestProgress
                            && !UtilIsProgressRequirementsMet(QuestId, QuestRequirementInLog, QuestRequirementComplete));
            }
        }


        public override void OnStart()
        {
            OnStart_QuestBehaviorCore();
        }
        #endregion


        #region Base class primitives

        /// <summary>
        /// <para>This reports problems, and stops BT processing if there was a problem with attributes...
        /// We had to defer this action, as the 'profile line number' is not available during the element's
        /// constructor call.</para>
        /// <para>It also captures the user's configuration, and installs Behavior Tree hooks.  The items will
        /// be restored when the behavior terminates, or Honorbuddy is stopped.</para>
        /// </summary>
        /// <param name="extraGoalTextDescription"></param>
        protected void OnStart_QuestBehaviorCore(string extraGoalTextDescription = null)
        {
            // Semantic coherency / covariant dependency checks...
            UsageCheck_SemanticCoherency(Element,
                ((QuestObjectiveIndex > 0) && (QuestId <= 0)),
                context => string.Format("QuestObjectiveIndex of '{0}' specified, but no corresponding QuestId provided",
                                        QuestObjectiveIndex));
            EvaluateUsage_SemanticCoherency(Element);

            // Deprecated attributes...
            EvaluateUsage_DeprecatedAttributes(Element);

            // This reports problems, and stops BT processing if there was a problem with attributes...
            // We had to defer this action, as the 'profile line number' is not available during the element's
            // constructor call.
            OnStart_HandleAttributeProblem();

            // If the quest is complete, this behavior is already done...
            // So we don't want to falsely inform the user of things that will be skipped.
            if (!IsDone)
            {
                // Monitored Behaviors...
                if (QuestBehaviorCoreSettings.Instance.MonitoredBehaviors.Contains(GetType().Name))
                {
                    QBCLog.Warning("MONITORED BEHAVIOR: {0}", GetType().Name);
                    AudibleNotifyOn(true);
                }

                // The ConfigMemento() class captures the user's existing configuration.
                // After its captured, we can change the configuration however needed.
                // When the memento is dispose'd, the user's original configuration is restored.
                // More info about how the ConfigMemento applies to saving and restoring user configuration
                // can be found here...
                //     http://www.thebuddyforum.com/mediawiki/index.php?title=Honorbuddy_Programming_Cookbook:_Saving_and_Restoring_User_Configuration
                _mementoSettings = new ConfigMemento();

                // Preserved (and restore on dispose) the BehaviorFlags , in case the child needs to change them...
                _behaviorFlagsOriginal = LevelBot.BehaviorFlags;

                BotEvents.OnBotStop += BotEvents_OnBotStop;

                if (Targeting.Instance != null)
                {
                    Targeting.Instance.IncludeTargetsFilter += TargetFilter_IncludeTargets;
                    Targeting.Instance.RemoveTargetsFilter += TargetFilter_RemoveTargets;
                    Targeting.Instance.WeighTargetsFilter += TargetFilter_WeighTargets;
                }

                Query.BlacklistsReset();

                // Add any local blackspots desired...
                // NB: Ideally, we'd save and restore the original blackspot list.  However,
                // BlackspotManager does not currently give us a way to "see" what is currently
                // on the list.
                _localBlackspots = BlackspotsType.GetOrCreate(Element, "Blackspots");
                BlackspotManager.AddBlackspots(_localBlackspots.GetBlackspots());

                UpdateGoalText(extraGoalTextDescription);

                _behaviorTreeHook_CombatMain = new ExceptionCatchingWrapper(this, CreateBehavior_CombatMain());
                TreeHooks.Instance.InsertHook("Combat_Main", 0, _behaviorTreeHook_CombatMain);
                _behaviorTreeHook_CombatOnly = new ExceptionCatchingWrapper(this, CreateBehavior_CombatOnly());
                TreeHooks.Instance.InsertHook("Combat_Only", 0, _behaviorTreeHook_CombatOnly);
                _behaviorTreeHook_DeathMain = new ExceptionCatchingWrapper(this, CreateBehavior_DeathMain());
                TreeHooks.Instance.InsertHook("Death_Main", 0, _behaviorTreeHook_DeathMain);
                _behaviorTreeHook_QuestbotMain = new ExceptionCatchingWrapper(this, CreateBehavior_QuestbotMain());
                TreeHooks.Instance.InsertHook("Questbot_Main", 0, _behaviorTreeHook_QuestbotMain);
            }
        }
        #endregion


        protected void BehaviorDone(string extraMessage = null)
        {
            if (!_isBehaviorDone)
            {
                QBCLog.DeveloperInfo("{0} behavior complete.  {1}", GetType().Name, (extraMessage ?? string.Empty));
                _isBehaviorDone = true;
            }
        }


        /// <summary>
        /// <para>This method should check for use of deprecated attributes by the profile.
        /// It should make calls to UsageCheck_DeprecatedAttribute() to accomplish the task.</para>
        /// </summary>
        /// <param name="xElement"></param>
        protected abstract void EvaluateUsage_DeprecatedAttributes(XElement xElement);
        //{
        //     // EXAMPLE: 
        //    UsageCheck_DeprecatedAttribute(xElement,
        //        Args.Keys.Contains("Nav"),
        //        "Nav",
        //        context => string.Format("Automatically converted Nav=\"{0}\" attribute into MovementBy=\"{1}\"."
        //                                  + "  Please update profile to use MovementBy, instead.",
        //                                  Args["Nav"], MovementBy));
        // }


        /// <summary>
        /// <para>This method should perform any semantic coherency, or covariant dependency checks needed
        /// by the behavior.  It should make calls to UsageCheck_SemanticCoherency() to accomplish the task.</para>
        /// </summary>
        /// <param name="xElement"></param>
        protected abstract void EvaluateUsage_SemanticCoherency(XElement xElement);
        //{
        //    // EXAMPLE:
        //    UsageCheck_SemanticCoherency(xElement,
        //        (!MobIds.Any() && !FactionIds.Any()),
        //        context => "You must specify one or more MobIdN, one or more FactionIdN, or both.");
        //
        //    const double rangeEpsilon = 3.0;
        //    UsageCheck_SemanticCoherency(xElement,
        //        ((RangeMax - RangeMin) < rangeEpsilon),
        //        context => string.Format("Range({0}) must be at least {1} greater than MinRange({2}).",
        //                      RangeMax, rangeEpsilon, RangeMin)); 
        //}



        #region TargetFilters
        /// <summary>
        /// <para>HBcore runs the TargetFilter_RemoveTargets before the TargetFilter_IncludeTargets.</para>
        /// </summary>
        /// <param name="units"></param>
        protected virtual void TargetFilter_IncludeTargets(List<WoWObject> incomingWowObjects, HashSet<WoWObject> outgoingWowObjects)
        {
            // We expect the child to override this behavior

            for (int i = incomingWowObjects.Count - 1; i >= 0; --i)
            {
                var wowObject = incomingWowObjects[i];

                try
                {
                    // Skip invalid objects...
                    if (!Query.IsViable(wowObject))
                        { continue; }

                    // Custom logic here...

                    outgoingWowObjects.Add(wowObject);
                }
                catch (System.AccessViolationException)
                {
                    // empty
                }
                catch (Styx.InvalidObjectPointerException)
                {
                    // empty
                }
            }
        }


        /// <summary>
        /// <para>HBcore runs the TargetFilter_RemoveTargets before the TargetFilter_IncludeTargets.</para>
        /// </summary>
        /// <param name="wowObjects"></param>
        protected virtual void TargetFilter_RemoveTargets(List<WoWObject> wowObjects)
        {
            // We expect the child to override this behavior

            for (int i = wowObjects.Count - 1; i >= 0; --i)
            {
                try
                {
                    var wowObject = wowObjects[i];

                    // Remove invalid units...
                    if (!Query.IsViable(wowObject))
                    {
                        wowObjects.RemoveAt(i);
                        continue;
                    }

                    // Custom logic here...
                }
                catch (Styx.InvalidObjectPointerException)
                {
                    wowObjects.RemoveAt(i);
                    continue;
                }
                catch (System.AccessViolationException)
                {
                    wowObjects.RemoveAt(i);
                    continue;
                }
            }
        }


        /// <summary>
        /// <para>When scoring targets, a higher value of TargetPriority.Score makes the target more valuable.</para>
        /// </summary>
        /// <param name="units"></param>
        protected virtual void TargetFilter_WeighTargets(List<Targeting.TargetPriority> targetPriorities)
        {
            // empty--left for child to override

            const float InvalidTargetScore = -1000000f;

            for (int i = targetPriorities.Count - 1; i >= 0; --i)
            {
                var priority = targetPriorities[i];

                try
                {
                    // Remove invalid units...
                    var wowUnit = priority.Object as WoWUnit;
                    if (!Query.IsViable(wowUnit))
                    {
                        priority.Score = InvalidTargetScore;
                        targetPriorities.RemoveAt(i);
                        continue;
                    }

                    // Custom weighting logic here...
                }
                catch (Styx.InvalidObjectPointerException)
                {
                    priority.Score = InvalidTargetScore;
                    targetPriorities.RemoveAt(i);
                    continue;
                }
                catch (System.AccessViolationException)
                {
                    priority.Score = InvalidTargetScore;
                    targetPriorities.RemoveAt(i);
                    continue;
                }
            }
        }
        #endregion


        #region Main Behaviors
        protected virtual Composite CreateBehavior_CombatMain()
        {
            return new PrioritySelector(
                // empty, for now...
                );
        }


        protected virtual Composite CreateBehavior_CombatOnly()
        {
            return new PrioritySelector(
                // empty, for now...
                );
        }


        protected virtual Composite CreateBehavior_DeathMain()
        {
            return new PrioritySelector(
                // empty, for now...
                );
        }


        protected virtual Composite CreateBehavior_QuestbotMain()
        {
            return new PrioritySelector(
                // empty, for now...
                );
        }


        protected virtual Composite CreateMainBehavior()
        {
            return new PrioritySelector(
                // empty, for now...
                );
        }
        #endregion


        #region Helpers
        public void UpdateGoalText(string extraGoalTextDescription)
        {
            PlayerQuest quest = StyxWoW.Me.QuestLog.GetQuestById((uint)QuestId);

            TreeRoot.GoalText = string.Format(
                "{1}: \"{2}\"{0}{3}{0}{0}{4}",
                Environment.NewLine,
                QBCLog.GetVersionedBehaviorName(this),
                ((quest != null)
                    ? string.Format("\"{0}\" (QuestId: {1})", quest.Name, QuestId)
                    : "In Progress (no associated quest)"),
                (extraGoalTextDescription ?? string.Empty),
                Utility.GetProfileReference(Element));
        }
        #endregion
    }
}