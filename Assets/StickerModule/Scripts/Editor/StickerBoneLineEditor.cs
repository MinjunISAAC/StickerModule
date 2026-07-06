using UnityEditor;
using UnityEngine;

namespace Gunter.Sticker.EditorTools
{
    // --------------------------------------------------
    // UI_StickerBoneLine 편집기.
    //  - 그리기 모드에서 씬을 클릭하면 스티커 평면 위에 선 점이 추가된다.
    //  - 각 점은 씬 핸들로 이동 가능.
    //  - Bone Count 만큼 선을 따라 본 위치(노란 점)를 미리보기.
    // --------------------------------------------------
    [CustomEditor(typeof(UI_StickerBoneLine))]
    public class StickerBoneLineEditor : Editor
    {
        private bool drawMode = false;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var line = (UI_StickerBoneLine)target;

            EditorGUILayout.Space();
            drawMode = GUILayout.Toggle(
                drawMode,
                drawMode ? "✏️ 그리기 모드: ON (씬에서 클릭해 점 추가)" : "그리기 모드 켜기",
                "Button");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("마지막 점 삭제"))
                {
                    Undo.RecordObject(line, "Remove Point");
                    line.RemoveLast();
                    EditorUtility.SetDirty(line);
                }
                if (GUILayout.Button("전체 지우기"))
                {
                    Undo.RecordObject(line, "Clear Points");
                    line.ClearPoints();
                    EditorUtility.SetDirty(line);
                }
            }

            EditorGUILayout.HelpBox(
                "그리기 모드를 켜고 스티커 위를 클릭하면 선 점이 추가됩니다.\n" +
                "점은 씬의 구(sphere) 핸들로 이동할 수 있고, 선을 따라 Bone Count 개의 본(노란 점)이 균등 분포됩니다.",
                MessageType.Info);

            // 베이크 상태 표시.
            EditorGUILayout.Space();
            var smr = line.GetComponentInChildren<SkinnedMeshRenderer>(true);
            bool baked = smr != null && smr.bones != null && smr.bones.Length > 0;
            EditorGUILayout.HelpBox(
                baked ? $"✓ 스킨 베이크 완료 (본 {smr.bones.Length}개)" : "아직 베이크 안 됨",
                baked ? MessageType.Info : MessageType.Warning);

            if (GUILayout.Button("Bake Skinned (이 오브젝트)"))
            {
                StickerMeshBaker.BakeSkinned(line.gameObject);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();
            }
        }

        private void OnSceneGUI()
        {
            var line = (UI_StickerBoneLine)target;
            var t = line.transform;
            var pts = line.EditablePoints;

            // 점 이동 핸들(줌에 따라 크기 일정하게).
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 world = t.TransformPoint(pts[i]);
                float hs = HandleUtility.GetHandleSize(world) * 0.08f;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(world, hs, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(line, "Move Point");
                    pts[i] = t.InverseTransformPoint(moved);
                    EditorUtility.SetDirty(line);
                }
            }

            // 선/본 미리보기는 Repaint 때만 그린다(깜빡임 방지).
            if (Event.current.type == EventType.Repaint)
            {
                Handles.color = Color.cyan;
                for (int i = 0; i < pts.Count - 1; i++)
                    Handles.DrawLine(t.TransformPoint(pts[i]), t.TransformPoint(pts[i + 1]), 3f);

                if (pts.Count >= 2)
                {
                    var bones = line.SampleBonePositions();
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Vector3 w = t.TransformPoint(bones[i]);
                        float u = bones.Length > 1 ? (float)i / (bones.Length - 1) : 0f;
                        Handles.color = Color.Lerp(Color.green, Color.red, u); // 시작=초록, 끝=빨강
                        float bs = HandleUtility.GetHandleSize(w) * 0.1f;
                        Handles.SphereHandleCap(0, w, Quaternion.identity, bs, EventType.Repaint);
                    }
                    Handles.color = Color.white;
                    Handles.Label(t.TransformPoint(bones[0]), " start");
                    Handles.Label(t.TransformPoint(bones[bones.Length - 1]), " end");
                }
            }

            // 그리기 모드: 클릭으로 점 추가.
            if (drawMode)
            {
                int id = GUIUtility.GetControlID(FocusType.Passive);
                HandleUtility.AddDefaultControl(id);

                Event e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    Plane plane = new Plane(t.forward, t.position); // 오브젝트 XY 평면
                    if (plane.Raycast(ray, out float enter))
                    {
                        Vector3 hit = ray.GetPoint(enter);
                        Undo.RecordObject(line, "Add Point");
                        line.AddPoint(t.InverseTransformPoint(hit));
                        EditorUtility.SetDirty(line);
                        e.Use();
                    }
                }
            }
        }
    }
}
