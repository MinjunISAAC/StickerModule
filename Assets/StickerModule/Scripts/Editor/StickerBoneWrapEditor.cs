using UnityEditor;
using UnityEngine;

namespace Gunter.Sticker.EditorTools
{
    // --------------------------------------------------
    // UI_StickerBoneWrap 편집기.
    //  - Play 없이 에디터에서 롤 애니메이션을 스크럽/재생해 확인.
    //  - ▶ Play Preview 는 EditorApplication.update 로 duration 만큼 실시간 재생.
    // --------------------------------------------------
    [CustomEditor(typeof(UI_StickerBoneWrap))]
    public class StickerBoneWrapEditor : Editor
    {
        private float previewP = 1f;
        private bool playing = false;
        private double startTime;

        private void OnEnable() { EditorApplication.update += OnEditorUpdate; }
        private void OnDisable() { EditorApplication.update -= OnEditorUpdate; }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var wrap = (UI_StickerBoneWrap)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("에디터 프리뷰", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            previewP = EditorGUILayout.Slider("Roll (0=말림, 1=평평)", previewP, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                playing = false;
                wrap.SamplePreview(previewP);
                SceneView.RepaintAll();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(playing ? "■ Stop" : "▶ Play Preview"))
                {
                    playing = !playing;
                    if (playing) startTime = EditorApplication.timeSinceStartup;
                }
                if (GUILayout.Button("Rolled")) { playing = false; previewP = 0f; wrap.SamplePreview(0f); SceneView.RepaintAll(); }
                if (GUILayout.Button("Flat")) { playing = false; previewP = 1f; wrap.SamplePreview(1f); SceneView.RepaintAll(); }
            }

            EditorGUILayout.HelpBox("Play Preview 는 Duration 만큼 실시간으로 재생됩니다. (SkinnedMeshRenderer 가 켜져 보입니다)", MessageType.Info);
        }

        private void OnEditorUpdate()
        {
            if (!playing || target == null) return;

            var wrap = (UI_StickerBoneWrap)target;
            float dur = wrap.PreviewDuration;
            float p = Mathf.Clamp01((float)(EditorApplication.timeSinceStartup - startTime) / dur);

            previewP = p;
            wrap.SamplePreview(p);
            Repaint();
            SceneView.RepaintAll();

            if (p >= 1f) playing = false;
        }
    }
}
