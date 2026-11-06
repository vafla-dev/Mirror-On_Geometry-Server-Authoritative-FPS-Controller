


# Mirror-Server-Authoritative-FPS-Controller
Open-source server-authoritative CharacterController for Unity Mirror Networking.

Features:
- ✅ Server authoritative movement
- ✅ Client prediction
- ✅ High ping support (tested up to 200 ms)
- ✅ CharacterController based
- ✅ Anti-cheat friendly
- ✅ Easy integration


# Supported Networking Solutions

- [Mirror](https://github.com/MirrorNetworking/Mirror)


# Info

- [How To Integrate](https://github.com/vafla-dev/Mirror-On_Geometry-Server-Authoritative-FPS-Controller/tree/main#how-to-integrate)
- [How It Works](https://github.com/vafla-dev/Mirror-On_Geometry-Server-Authoritative-FPS-Controller/tree/main#how-it-works)
- [Customization & Extending Logic](https://github.com/vafla-dev/Mirror-On_Geometry-Server-Authoritative-FPS-Controller/tree/main#customization--extending-logic)

# How To Integrate

1 [Download](https://github.com/vafla-dev/Mirror-On_Geometry-Server-Authoritative-FPS-Controller/archive/refs/heads/main.zip) as a ZIP file.
2 Unpack the file and move it into the Unity editor.
3 Your project must include [Mirror](https://github.com/MirrorNetworking/Mirror).
4 Add the **Character Controller** component to your player prefab.
5 Add the [Mirror FPS Controller.cs](https://github.com/vafla-dev/Mirror-On_Geometry-Server-Authoritative-FPS-Controller/blob/main/Scripts/Mirror%20FPS%20Controller.cs) component to your player prefab.
6 Add your camera's rotation object to the "Rot Main Camera" field.
- example:

https://github.com/user-attachments/assets/d5dc8ba1-9b51-4422-b4bc-3325f917ca59

# Customization & Extending Logic

- Basic knowledge of C# is required for the successful integration of the script into the project.

* **`CenMove()`** – Return `false` to block player movement (e.g., when the inventory, chat, or pause menu is open).
* **`CenRotate()`** – Return `false` to block camera rotation (e.g., during shop interactions or cutscenes).
* **`GravityCanBeCalculated(bool isServer)`** – Return `false` to pause gravity logic. Useful for flight modes, swimming, or admin NoClip mechanics.

To integrate these conditions, create a custom script that inherits from [`Mirror FPS Controller.cs`](https://github.com/vafla-dev/Mirror-On_Geometry-Server-Authoritative-FPS-Controller/blob/main/Scripts/Mirror%20FPS%20Controller.cs):
```csharp
using UnityEngine;

public class PlayerController : MirrorFPSController
{
    public bool fly = true;
    public bool isInventoryOpen = true;
    public bool stunned = true;

    protected override bool CanMove()
    {
        // Player cannot move if inventory is open
        if(isInventoryOpen) return false;

        return true;
    }

    protected override bool CanRotate()
    {
        //The player cannot rotate the camera while stunned
        if (stunned) return false;

        return true;
    }

    protected override bool GravityCanBeCalculated(bool isServer = false)
    {
        //Gravity is disabled for the player if flight mode is enabled
        if(fly) return false;

        return true;
    }
}
```


# How It Works

- This controller is unlike the ones used in popular games.
- The client does not transmit input data; instead, it transmits two positions: its own and the one where the so-called "Target" is intended to be:
  ```csharp
  CmdSetMoveTarget(transform.position, transform.position + vector);
  ```
- The server, in turn, does not move the player immediately; it simply stores this data:
   ```csharp
     [Command]
     private void CmdSetMoveTarget(Vector3 currentPos, Vector3 target)
     {
         currentPosForServer = currentPos;
         targetForServer = target;
     }
   ```
- In the next frame, the server does the following:
  - The server first determines how far it can move during this frame:
  - ```csharp
    Vector3 directional = targetForServer - transform.position;
    if (Vector3.Distance(directional, Vector3.zero) > 1)
        directional.Normalize();
    directional = directional * speedMovement * smoothMovementOnServer * Time.deltaTime;
    ```
  - Also, since the input is calculated on the client using a simple "Input.GetAxis()", the server simulates its behavior:
  - ```csharp
    float to = 0.1f;

    if (Vector3.Distance(targetForServer, transform.position) > 0.01)
    {
        to = Vector3.Distance(directional, Vector3.zero);
    }

    smoothMovementOnServer = Mathf.MoveTowards(smoothMovementOnServer, to, 3 * Time.deltaTime);
    ```
  - Next, it passes this data to the so-called "P" method, which effectively draws a line between the client's current position and the target.
  - ```csharp
    if (Vector3.Distance(directional, Vector3.zero) < Vector3.Distance(targetForServer - transform.position, Vector3.zero))
        P(transform.position,
            currentPosForServer,
            targetForServer,
            Vector3.Distance(directional, Vector3.zero),
            ref directional);
    ```
  - The P-method is also executed only when the server cannot reach the target for this frame.
  - After method P draws the line, it creates a circle with a radius equal to the distance the server can cover in that frame.
  - Next, the method checks whether the circle intersects the line segment between the current position and the target.
  - If they intersect, the point closest to the target is selected, and the direction to it is recorded in `ref Vector3 delta`.
  - If there is no intersection point, the direction to the nearest point on the line segment is recorded.
  - ```csharp
    bool P(Vector3 point, Vector3 startPoint, Vector3 endPoint, float radius, ref Vector3 delta)
    {
        Vector3 M = startPoint - point;
        Vector3 N = endPoint - startPoint;

        float a = (N.x * N.x) + (N.y * N.y) + (N.z * N.z);
        float b = (2 * M.x * N.x) + (2 * M.y * N.y) + (2 * M.z * N.z);
        float c = (M.x * M.x) + (M.y * M.y) + (M.z * M.z) - radius * radius;

        //at² + bt + c = 0

        float d = b * b - 4 * a * c;

        if (d < 0)
            return false;

        d = Mathf.Sqrt(d);

        float[] x = new float[2];

        x[0] = (-b - d) / (2 * a);
        x[1] = (-b + d) / (2 * a);

        float t = -1;

        foreach (float value in x)
        {
            if (value >= 0 && value <= 1)
                if (value > t)
                    t = value;
        }
        if (t >= 0 && t <= 1)
        {
            delta = startPoint + N * t - point;

            return true;
        }

        t = Vector3.Dot(point - startPoint, N) / N.sqrMagnitude;

        if (t >= 0 && t <= 1)
        {
            delta = startPoint + N * t - point;

            delta.Normalize();

            delta *= radius;

            return true;
        }

        return false;
    }
    ```
  - And finally, the server actually moves the client and sends the position to the others:
  - ```csharp
        CalculateGravityByServer(ref directional);

        characterController.Move(directional);

        RpcSetPos(transform.position, gravity);
      ```
   - Furthermore, the "CalculateGravityByServer" method completely overwrites the Y-axis value, making it impossible to use flight cheats.
- Also, with this approach, even if a client sends strange data—such as a distant target or a position inside a wall—the server still won't allow movement into the wall. Furthermore, the target has no impact on velocity, ensuring that other clients always receive the correct position.
- And if the client finds out that their position is incorrect, they will return it:
- ```csharp
  [ClientRpc]
  private void RpcSetPos(Vector3 pos, float gravity)
  {
      if (isLocalPlayer)
      {
          Vector3 diff = pos - transform.position;

          //The part about gravity has been cut from the demonstration.
  
          bool thatIsCorrect = (diff.x * diff.x + diff.z * diff.z) <= (maximumPermissibleDifferenceInPosition * maximumPermissibleDifferenceInPosition);
          if (thatIsCorrect) return;

          characterController.enabled = false;

          transform.position = pos;

          characterController.enabled = true;

          return;
    }
    if (characterController != null)
        characterController.enabled = false;

    transform.position = pos;

    if (characterController != null)
        characterController.enabled = true;
    }
  ```


-Illustration of server-side logic operation

<img width="432" height="432" alt="example of work7-22-2026_20-25-30" src="https://github.com/user-attachments/assets/53b20b31-fbde-4f27-9ab0-092a216a7b43" />

**Note: The illustration shows an impossible scenario where the client stops sending data right in the middle of the process; this was omitted for the sake of clearly demonstrating the server-side logic.**
