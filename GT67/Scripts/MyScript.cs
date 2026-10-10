/// <summary>Makes a point light follow this object.</summary>
  public class MyScript : Script
  {
      public float Speed = 1f;

      private PointLight? _light;   // a field: OnStart and OnUpdate can both use it

      public override void OnStart()
      {
          foreach (var light in Scene.PointLights)
              if (light.Name == "Point Light 3") _light = light;
      }

      public override void OnUpdate(float deltaTime)
      {
          if (_light != null) _light.Position = Self.WorldPosition;
      }
  }