using Content.Shared.Actions;
using Content.Shared.Gravity;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Movement.Systems;

public abstract class SharedJetpackSystem : EntitySystem
{
    [Dependency] protected readonly SharedContainerSystem Container = default!;
    [Dependency] protected readonly IMapManager MapManager = default!;
    [Dependency] protected readonly MovementSpeedModifierSystem MovementSpeedModifier = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JetpackComponent, GetItemActionsEvent>(OnJetpackGetAction);
        SubscribeLocalEvent<JetpackComponent, DroppedEvent>(OnJetpackDropped);
        SubscribeLocalEvent<JetpackComponent, ToggleJetpackEvent>(OnJetpackToggle);
        SubscribeLocalEvent<JetpackUserComponent, CanWeightlessMoveEvent>(OnJetpackUserCanWeightless);
        SubscribeLocalEvent<JetpackUserComponent, MobMovementProfileEvent>(OnJetpackUserMovement);
        SubscribeLocalEvent<JetpackUserComponent, EntParentChangedMessage>(OnJetpackUserEntParentChanged);
        SubscribeLocalEvent<GravityChangedMessage>(OnJetpackUserGravityChanged);
    }

    private void OnJetpackDropped(EntityUid uid, JetpackComponent component, DroppedEvent args)
    {
        SetEnabled(component, false, args.User);
    }

    private void OnJetpackUserMovement(EntityUid uid, JetpackUserComponent component, ref MobMovementProfileEvent args)
    {
        // Only overwrite jetpack movement if they're offgrid.
        if (args.Override || !args.Weightless) return;

        args.Override = true;
        args.Acceleration = component.Acceleration;
        args.WeightlessModifier = component.WeightlessModifier;
        args.Friction = component.Friction;
    }

    private void OnJetpackUserCanWeightless(EntityUid uid, JetpackUserComponent component, ref CanWeightlessMoveEvent args)
    {
        args.CanMove = true;
    }

    private void OnJetpackUserEntParentChanged(EntityUid uid, JetpackUserComponent component, ref EntParentChangedMessage args)
    {
        MovementSpeedModifier.RefreshMovementSpeedModifiers(component.Owner);
    }

    private void OnJetpackUserGravityChanged(GravityChangedMessage ev)
    {
        var gridUid = ev.ChangedGridIndex;
        foreach (var (_, transform) in EntityQuery<JetpackUserComponent, TransformComponent>(true))
        {
            if(transform.GridUid == gridUid)
                MovementSpeedModifier.RefreshMovementSpeedModifiers(transform.Owner);
        }
    }

    private void SetupUser(EntityUid uid, JetpackComponent component)
    {
        var user = EnsureComp<JetpackUserComponent>(uid);
        user.Acceleration = component.Acceleration;
        user.Friction = component.Friction;
        user.WeightlessModifier = component.WeightlessModifier;
    }

    private void OnJetpackToggle(EntityUid uid, JetpackComponent component, ToggleJetpackEvent args)
    {
        if (args.Handled) return;

        SetEnabled(component, !IsEnabled(uid));
    }

    private void OnJetpackGetAction(EntityUid uid, JetpackComponent component, GetItemActionsEvent args)
    {
        args.Actions.Add(component.ToggleAction);
    }

    private bool IsEnabled(EntityUid uid)
    {
        return HasComp<ActiveJetpackComponent>(uid);
    }

    public void SetEnabled(JetpackComponent component, bool enabled, EntityUid? user = null)
    {
        if (IsEnabled(component.Owner) == enabled ||
            enabled && !CanEnable(component)) return;

        if (enabled)
        {
            EnsureComp<ActiveJetpackComponent>(component.Owner);
        }
        else
        {
            RemComp<ActiveJetpackComponent>(component.Owner);
        }

        if (user == null)
        {
            Container.TryGetContainingContainer(component.Owner, out var container);
            user = container?.Owner;
        }

        // Can't activate if no one's using.
        if (user == null && enabled) return;

        if (user != null)
        {
            if (enabled)
            {
                SetupUser(user.Value, component);
            }
            else
            {
                RemComp<JetpackUserComponent>(user.Value);
            }

            MovementSpeedModifier.RefreshMovementSpeedModifiers(user.Value);
        }

        TryComp<AppearanceComponent>(component.Owner, out var appearance);
        appearance?.SetData(JetpackVisuals.Enabled, enabled);
        Dirty(component);
    }

    public bool IsUserFlying(EntityUid uid)
    {
        return HasComp<JetpackUserComponent>(uid) &&
               TryComp<PhysicsComponent>(uid, out var physicsComponent) &&
               uid.IsWeightless(physicsComponent, mapManager: MapManager, entityManager: EntityManager);
    }

    protected abstract bool CanEnable(JetpackComponent component);

    [Serializable, NetSerializable]
    protected sealed class JetpackComponentState : ComponentState
    {
        public bool Enabled;
    }
}

[Serializable, NetSerializable]
public enum JetpackVisuals : byte
{
    Enabled,
}
