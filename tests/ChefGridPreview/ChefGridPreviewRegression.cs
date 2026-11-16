using System;
using OC2DIYChef;
using UnityEngine;

static class ChefGridPreviewRegression
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }

    static FrontendChefCustomisation Source()
    {
        var actor = new GameObject("Original chef");
        var source = actor.AddComponent<FrontendChefCustomisation>();
        source.m_chef = actor.AddComponent<ChefMeshReplacer>();
        source.m_chef.m_chefTemplatePrefab = new GameObject("Native prefab");
        var hats = actor.AddComponent<HatMeshVisibility>();
        hats.m_meshes = new object(); hats.m_stateFlags = new object();
        var view = actor.AddComponent<FrontendChef>();
        view.m_StandardSkinShader = new object(); view.m_StandardClothesShader = new object();
        view.m_UISkinShader = new object(); view.m_UIClothesShader = new object();
        return source;
    }

    static void Main()
    {
        checks += ChefGridHeadingRegression.Run();
        var source = Source();
        var thumbnail = new ChefGridPreviewScene(source, new Vector3(10000, -10000, 10000), new Color(0, 0, 0, 0));
        var live = new ChefGridPreviewScene(source, new Vector3(10100, -10000, 10000), new Color(1, 0.9f, 0.9f));
        Check(thumbnail.Stage != live.Stage && thumbnail.Actor != live.Actor && thumbnail.Camera != live.Camera,
            "thumbnail and animated renderers own independent scenes");
        Check(thumbnail.Actor.transform.localRotation.Equals(Quaternion.Euler(0, -21.6362f, 0)) &&
            live.Actor.transform.localRotation.Equals(thumbnail.Actor.transform.localRotation),
            "static and animated chefs both use the fixed native angle without caller settings");
        var placeholder = GameObject.Created.FindLast(item => item.name == "Chef" && item.transform.parent == live.Actor.transform);
        Check(placeholder.transform.localScale.x == 1 && placeholder.transform.localScale.y == 1 &&
            placeholder.transform.localScale.z == 1,
            "snapshot-based preview actors must retain the known-working unit scale");
        Check(live.Camera.nearClipPlane == 0.01f && live.Camera.farClipPlane == 30,
            "preview clip planes retain the working snapshot and live framing units");
        Check(thumbnail.Stage.transform.position.x != live.Stage.transform.position.x &&
            live.Actor.transform.parent == live.Stage.transform,
            "chef instances stay attached to their isolated stage");
        Check(live.Replacer.m_chefTemplatePrefab == source.m_chef.m_chefTemplatePrefab &&
            live.Actor.GetComponent<ChefGridPortraitActor>() != null,
            "preview uses the native chef template and retains the integration marker");
        var originalView = source.GetComponent<FrontendChef>();
        Check(live.View.m_StandardSkinShader == originalView.m_StandardSkinShader &&
            live.View.m_StandardClothesShader == originalView.m_StandardClothesShader &&
            live.View.m_UISkinShader == originalView.m_UISkinShader &&
            live.View.m_UIClothesShader == originalView.m_UIClothesShader && live.View.Ambient.r == 0.8f,
            "both preview kinds inherit native shaders and UI lighting");
        Check(live.Actor.GetComponent<HatMeshVisibility>().m_meshes == source.GetComponent<HatMeshVisibility>().m_meshes,
            "native hat visibility metadata is copied onto the preview actor");
        Check(!live.Camera.enabled && live.Camera.orthographic && live.Camera.cullingMask == ((1 << live.Actor.layer) | (1 << HatData.hatLayer)) &&
            live.Camera.backgroundColor.a == 1 && thumbnail.Camera.backgroundColor.a == 0,
            "manual cameras retain the caller's background and include the hat layer");

        var small = thumbnail.CreateTarget(128, RenderTextureFormat.ARGB32);
        var large = live.CreateTarget(512, RenderTextureFormat.RGB111110Float);
        thumbnail.Camera.targetTexture = small; live.Camera.targetTexture = large;
        var model = new GameObject("Loaded chef"); live.View.ChefModel = model;
        var audio = model.AddComponent<AudioSource>();
        var events = model.AddComponent<AnimatorAudioComponent>();
        var rumble = model.AddComponent<AnimatorRumbleComponent>();
        live.SilenceModel();
        Check(!audio.enabled && !events.enabled && !rumble.enabled, "preview animation produces no sound or rumble");
        var animator = model.AddComponent<Animator>();
        live.View.AttachModel(model);
        live.PlaySelectingAnimation(0);
        Check(animator.AnimationValue == 1,
            "a loaded preview without native customisation must enter P1 animation instead of leaving AnimationSet at zero");
        Check(animator.TriggerCalls == 1 && animator.LastTrigger == Animator.StringToHash("TrigChooseChef"),
            "preview starts the native choosing-chef state instead of stopping at the generic van idle");
        live.PlaySelectingAnimation(3);
        Check(animator.AnimationValue == 4, "the native method retains each local player's animation group");
        var replacement = new GameObject("Replacement chef");
        var replacementAnimator = replacement.AddComponent<Animator>(); replacementAnimator.HasAnimationSet = false;
        replacementAnimator.HasSelectingTrigger = false;
        live.View.AttachModel(replacement);
        live.PlaySelectingAnimation(0);
        Check(replacementAnimator.AnimationValue == 0 && !live.View.m_bHasAnimationSet && replacementAnimator.TriggerCalls == 0,
            "model replacement rechecks capabilities and does not invent a missing controller parameter");
        live.View.AttachModel(model); live.PlaySelectingAnimation(0);
        Check(animator.AnimationValue == 1 && live.View.m_bHasAnimationSet,
            "returning to a supported model initializes its native animation group again");

        var visible = model.AddComponent<Renderer>();
        var disabled = model.AddComponent<Renderer>(); disabled.enabled = false;
        model.AddComponent<ParticleSystemRenderer>();
        var mask = new ChefGridAlphaMask(live.Camera);
        var commands = live.Camera.Commands; var maskMaterial = Material.Last;
        mask.Prepare(model);
        Check(commands.Draws == 2 && commands.Renderers.TrueForAll(item => item == visible) && maskMaterial.Mask == 1,
            "alpha repair draws all visible submeshes into alpha only, skipping disabled geometry and particles");
        visible.enabled = false; mask.Prepare(model);
        Check(commands.Draws == 0, "each frame discards stale alpha draws when a chef or hat becomes hidden");
        visible.enabled = true; model.layer = 12; mask.Prepare(model);
        Check(commands.Draws == 0, "alpha repair honors the preview camera's layer mask");
        model.layer = 0; model.SetActive(false); mask.Prepare(model);
        Check(commands.Draws == 0, "inactive models cannot leave an opaque silhouette");
        mask.Dispose(); mask.Dispose();
        Check(live.Camera.Commands == null && commands.Releases == 1 && maskMaterial.DestroyCalls == 1 &&
            large.DestroyCalls == 0, "alpha cleanup detaches and releases only its own buffer and material exactly once");
        Shader.Available = false;
        bool missingShader = false;
        try { new ChefGridAlphaMask(live.Camera); } catch (InvalidOperationException) { missingShader = true; }
        Check(missingShader && live.Camera.Commands == null, "missing mask shader leaves no attached render commands");
        Shader.Available = true;

        live.Dispose();
        Check(live.Camera.targetTexture == null && large.Releases == 1 && large.DestroyCalls == 1 &&
            live.Stage.DestroyCalls == 1 && !live.Stage.activeSelf && ChefLoading.Cancelled.Contains(live.Replacer),
            "closing a scene cancels its deferred load, detaches the target and releases its owned objects");
        Check(small.DestroyCalls == 0 && thumbnail.Stage.activeSelf && source.gameObject.activeSelf,
            "disposing one preview leaves the other preview and source chef intact");
        int cancellations = ChefLoading.Cancelled.Count;
        live.Dispose();
        Check(large.Releases == 1 && ChefLoading.Cancelled.Count == cancellations, "repeated cleanup is idempotent");
        bool rejected = false;
        try { live.CreateTarget(512, RenderTextureFormat.RGB111110Float); }
        catch (ObjectDisposedException) { rejected = true; }
        Check(rejected, "a disposed scene cannot allocate a leaked GPU target");

        RenderTexture.FailCreate = true;
        bool failed = false;
        try { thumbnail.CreateTarget(128, RenderTextureFormat.ARGB32); }
        catch (InvalidOperationException) { failed = true; }
        var partialTarget = RenderTexture.Last;
        thumbnail.Dispose();
        Check(failed && partialTarget.Releases == 1 && partialTarget.DestroyCalls == 1 && small.Releases == 1,
            "cleanup also owns targets whose allocation threw");
        RenderTexture.FailCreate = false;

        var broken = new GameObject("Missing native replacer").AddComponent<FrontendChefCustomisation>();
        int firstCreated = GameObject.Created.Count;
        failed = false;
        try { new ChefGridPreviewScene(broken, new Vector3(), new Color()); }
        catch (Exception) { failed = true; }
        var partialStage = GameObject.Created[firstCreated];
        Check(failed && partialStage.DestroyCalls == 1 && !partialStage.activeSelf,
            "failed native setup removes the partial offscreen scene before propagating the error");
        Console.WriteLine("PASS: " + checks + " preview scene setup and resource ownership checks; no game process used.");
    }
}
