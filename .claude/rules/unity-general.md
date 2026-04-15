---
paths:
  - "Assets/Scripts/**/*.cs"
---

# Unity C# General Standards

## Lifecycle Awareness
- Awake() for self-initialization, Start() for cross-references
- OnEnable()/OnDisable() for event subscribe/unsubscribe
- Never call GetComponent() in Update() — cache in Awake/Start
- Use CompareTag("tag") instead of gameObject.tag == "tag"
- Null-check before accessing potentially destroyed objects

## Performance
- Cache component references in Awake/Start
- Use object pooling for frequently created/destroyed objects
- Avoid string concatenation in Update — use StringBuilder
- Avoid LINQ in hot paths (Update, FixedUpdate)
- Use [SerializeField] private over public for inspector fields

## Architecture
- Singleton pattern: Manager.Instance is the hub
- MonoBehaviour for Unity lifecycle, plain C# classes for data
- Keep serializable data models separate from behavior
- Platform code isolated in bridge classes (AndroidBridge, IOSBridge)
- Single scene architecture — all UI canvas-based, toggled with SetActive

## Common Pitfalls to Avoid
- Don't use `new` to create MonoBehaviours — use AddComponent or Instantiate
- Don't access .gameObject or .transform of destroyed objects
- Don't mix coroutines with async/await in MonoBehaviours
- Don't forget to StopCoroutine when disabling objects
- Don't use Resources.Load for assets — use SerializeField references or Addressables
- Don't use Find()/FindObjectOfType() at runtime — cache references

## Naming
- PascalCase: classes, methods, properties, public fields
- camelCase: local variables, parameters, private fields
- _camelCase or camelCase with [SerializeField]: private fields shown in inspector
