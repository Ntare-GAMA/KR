using System.Collections.Generic;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // One map display. Heading-up follows the player (the map turns so the way you face is up: satnav screen,
    // minimap, map on your hand); north-up shows the whole play area with place names (the full map).
    // The image is CityMap's (OSM roads and buildings, drawn at build time); the route comes from the Navigator.
    public sealed class MapView : MonoBehaviour
    {
        public enum Mode { HeadingUp, NorthUp }

        Navigator _nav;
        Mode _mode;
        float _across;
        RectTransform _viewport, _content;
        RawImage _image;
        RouteLine _route;
        MapMarker _player, _dest;
        Text _bar;
        readonly List<(RectTransform label, Vector3 world)> _labels = new List<(RectTransform, Vector3)>();
        readonly List<Vector2> _uv = new List<Vector2>();
        int _version = -1, _progress = -1;
        float _routeTimer;

        // Builds a map view filling parent. metresAcross: how much of the city fits across (heading-up only).
        public static MapView Create(RectTransform parent, Navigator nav, Mode mode, float metresAcross, int barFont, bool labels)
        {
            var go = new GameObject($"Map ({mode})", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt);
            var view = go.AddComponent<MapView>();
            view.Build(rt, nav, mode, metresAcross, barFont, labels);
            return view;
        }

        static void Stretch(RectTransform r, float top = 0f)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = new Vector2(0f, -top);
        }

        static T Child<T>(Transform parent, string name) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        void Build(RectTransform root, Navigator nav, Mode mode, float metresAcross, int barFont, bool labels)
        {
            _nav = nav;
            _mode = mode;
            _across = metresAcross;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var frame = Child<Image>(root, "Frame");
            frame.color = new Color(0.04f, 0.05f, 0.06f, 0.92f);
            Stretch(frame.rectTransform);

            float barHeight = barFont > 0 ? barFont * 1.9f : 0f;
            _viewport = Child<RectMask2D>(root, "Viewport").rectTransform;
            Stretch(_viewport, barHeight);

            _content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(_viewport, false);
            _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 0.5f);
            _content.anchoredPosition = Vector2.zero;

            _image = Child<RawImage>(_content, "City");
            Stretch(_image.rectTransform);
            _route = Child<RouteLine>(_content, "Route");
            Stretch(_route.rectTransform);
            _route.color = new Color(0.15f, 0.75f, 1f, 0.95f);
            _route.raycastTarget = false;
            _dest = Child<MapMarker>(_content, "Destination");
            _dest.Kind = MapMarker.Shape.Diamond;
            _dest.color = new Color(1f, 0.35f, 0.25f);
            _dest.rectTransform.sizeDelta = new Vector2(22f, 22f);

            if (labels && nav != null)
                foreach (var (name, pos) in nav.Places)
                {
                    var t = Child<Text>(_content, $"Label {name}");
                    t.font = font;
                    t.fontSize = 18;
                    t.color = Color.white;
                    t.alignment = TextAnchor.LowerCenter;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    t.text = name;
                    t.rectTransform.sizeDelta = new Vector2(240f, 26f);
                    t.rectTransform.pivot = new Vector2(0.5f, -0.4f);
                    var outline = t.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                    _labels.Add((t.rectTransform, pos));
                }

            _player = Child<MapMarker>(mode == Mode.HeadingUp ? _viewport : _content, "You");
            _player.Kind = MapMarker.Shape.Arrow;
            _player.color = new Color(1f, 0.92f, 0.2f);
            _player.rectTransform.sizeDelta = new Vector2(20f, 26f);
            if (mode == Mode.HeadingUp)
                _player.rectTransform.anchorMin = _player.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            if (barFont > 0)
            {
                _bar = Child<Text>(root, "Directions");
                _bar.font = font;
                _bar.fontSize = barFont;
                _bar.color = Color.white;
                _bar.alignment = TextAnchor.MiddleLeft;
                _bar.horizontalOverflow = HorizontalWrapMode.Wrap;
                _bar.verticalOverflow = VerticalWrapMode.Truncate;
                var r = _bar.rectTransform;
                r.anchorMin = new Vector2(0f, 1f);
                r.anchorMax = Vector2.one;
                r.pivot = new Vector2(0.5f, 1f);
                r.offsetMin = new Vector2(8f, -barHeight);
                r.offsetMax = new Vector2(-8f, 0f);
            }
        }

        void LateUpdate()
        {
            var map = CityMap.Instance;
            if (map == null || _nav == null || _viewport.rect.width <= 0f)
                return;
            if (_image.texture != map.Texture)
                _image.texture = map.Texture;

            Vector2 size = map.SizeMetres;
            float across = _mode == Mode.NorthUp ? Mathf.Max(size.x, size.y * _viewport.rect.width / Mathf.Max(1f, _viewport.rect.height)) : _across;
            float px = _viewport.rect.width / across;
            _content.sizeDelta = size * px;
            Vector2 you = map.ToUV(_nav.PlayerPosition);
            float heading = _nav.PlayerHeading;

            if (_mode == Mode.HeadingUp)
            {
                _content.pivot = you;
                _content.localRotation = Quaternion.Euler(0f, 0f, heading);
            }
            else
            {
                _content.pivot = new Vector2(0.5f, 0.5f);
                _content.localRotation = Quaternion.identity;
                Place(_player.rectTransform, you);
                _player.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -heading);
                foreach (var (label, world) in _labels)
                    Place(label, map.ToUV(world));
            }

            var dest = _nav.Destination;
            _dest.enabled = dest != null;
            if (dest != null)
            {
                Place(_dest.rectTransform, map.ToUV(dest.Value));
                _dest.rectTransform.localRotation = Quaternion.Inverse(_content.localRotation);
            }

            _routeTimer -= Time.deltaTime;
            if (_nav.RouteVersion != _version || _nav.Progress != _progress || _routeTimer <= 0f)
            {
                _version = _nav.RouteVersion;
                _progress = _nav.Progress;
                _routeTimer = 0.25f;
                _uv.Clear();
                var route = _nav.Route;
                if (dest != null && route.Count > 1)
                {
                    _uv.Add(you);
                    for (int i = Mathf.Min(_progress + 1, route.Count - 1); i < route.Count; i++)
                        _uv.Add(map.ToUV(route[i]));
                }
                _route.Width = Mathf.Clamp(px * 7f, 3f, 9f);
                _route.SetPoints(_uv);
            }

            if (_bar != null)
                _bar.text = _nav.Instruction ?? (_nav.Places.Count > 0 ? "No destination. N / right grip: choose a place." : "");
        }

        static void Place(RectTransform r, Vector2 uv)
        {
            r.anchorMin = r.anchorMax = uv;
            r.anchoredPosition = Vector2.zero;
        }
    }
}
