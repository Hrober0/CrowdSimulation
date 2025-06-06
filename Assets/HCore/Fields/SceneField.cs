using UnityEditor;
using UnityEngine;

namespace HCore
{
    [System.Serializable]
    public class SceneField : ISerializationCallbackReceiver
    {
#if UNITY_EDITOR
        [SerializeField] private Object _sceneAsset = null;
        bool IsValidSceneAsset
        {
            get
            {
                if (_sceneAsset == null)
                    return false;
                return _sceneAsset.GetType().Equals(typeof(SceneAsset));
            }
        }
#endif

        [SerializeField]
        private string _scenePath = string.Empty;

        public string ScenePath
        {
            get
            {
#if UNITY_EDITOR
                var path = GetScenePathFromAsset();
                _scenePath = path;
                return path;
#else
                return _scenePath;
#endif
            }
        }

        public string SceneName => HFormat.GetNameFromPath(ScenePath);

        public static implicit operator string(SceneField sceneReference)
        {
            return sceneReference.ScenePath;
        }

        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            HandleBeforeSerialize();
#endif
        }

        public void OnAfterDeserialize()
        {
#if UNITY_EDITOR
            EditorApplication.update += HandleAfterDeserialize;
#endif
        }


#if UNITY_EDITOR
        private SceneAsset GetSceneAssetFromPath()
        {
            if (string.IsNullOrEmpty(_scenePath))
                return null;
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(_scenePath);
        }

        private string GetScenePathFromAsset()
        {
            if (_sceneAsset == null)
                return string.Empty;
            return AssetDatabase.GetAssetPath(_sceneAsset);
        }

        private void HandleBeforeSerialize()
        {
            if (IsValidSceneAsset == false && string.IsNullOrEmpty(_scenePath) == false)
            {
                _sceneAsset = GetSceneAssetFromPath();
                if (_sceneAsset == null)
                    _scenePath = string.Empty;

                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            }
            else
            {
                _scenePath = GetScenePathFromAsset();
            }
        }

        private void HandleAfterDeserialize()
        {
            EditorApplication.update -= HandleAfterDeserialize;
            // Asset is valid, don't do anything - Path will always be set based on it when it matters
            if (IsValidSceneAsset)
                return;

            // Asset is invalid but have path to try and recover from
            if (string.IsNullOrEmpty(_scenePath) == false)
            {
                _sceneAsset = GetSceneAssetFromPath();
                // No asset found, path was invalid. Make sure we don't carry over the old invalid path
                if (_sceneAsset == null)
                    _scenePath = string.Empty;

                if (Application.isPlaying == false)
                    UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            }
        }
#endif
    }
}