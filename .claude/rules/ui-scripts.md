---
paths:
  - "Assets/Scripts/UI/**/*.cs"
  - "Assets/Scripts/Main/BotSettings.cs"
  - "Assets/Scripts/Main/Manager.cs"
  - "Assets/Prefabs/**"
---

# Unity UI Development Standards

## Visual Design Quality
- Design like a senior mobile UI/UX designer — polished, production-grade
- Spacing system: multiples of 4 (4, 8, 12, 16, 24, 32, 48, 64)
- Typography: Title 24-28 bold, Subtitle 18-20 semibold, Body 14-16 regular, Caption 12
- Use subtle shadows, rounded corners (RoundedCorners package), and proper visual hierarchy
- Colors: derive from existing palette in project — check existing prefabs first
- Never ship placeholder/wireframe quality UI

## Components
- TextMeshProUGUI (TMPro) for ALL text — never legacy Text
- DOTween for ALL animations — never Animator for simple UI transitions
  - Page transitions: `.DOAnchorPos()` or `.DOFade()` with 0.25-0.35s duration
  - Button press: `.DOPunchScale(Vector3.one * 0.05f, 0.2f)` for tactile feedback
  - List items: stagger with `.SetDelay(index * 0.05f)` for cascade effect
- RoundedCorners for card-style containers
- CanvasGroup for fade transitions and interactability control

## Layout
- Always use anchors — never hardcoded pixel positions
- VerticalLayoutGroup / HorizontalLayoutGroup for lists and rows
- ContentSizeFitter for dynamic content
- ScrollRect with viewport mask for scrollable content
- Safe area: account for notch/cutout on all pages

## Mobile-First
- Minimum touch target: 44x44 dp (approximately 88x88 px at 2x)
- Primary actions in thumb zone (bottom 1/3 of screen)
- Font minimum: 14sp for body text, 12sp for captions only
- Consider one-handed operation for common flows

## Code Pattern
- All UI references: `[SerializeField] private` — never public fields
- Page switching: `GameObject.SetActive()` — never Instantiate/Destroy for pages
- Manager.Instance for navigation between pages
- Singleton MonoBehaviour pattern for page controllers
