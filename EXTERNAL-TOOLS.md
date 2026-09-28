# External tools setup (Zink / Magpie)

> This is one successful case, not a recommendation or support.
> No warranty.

## Zink (Mesa)

1. Download `mesa3d-26.0.6-release-msvc.7z`:
   `https://github.com/pal1000/mesa-dist-win/releases/download/26.0.6/mesa3d-26.0.6-release-msvc.7z`
2. Extract it
3. From `mesa3d-26.0.6-release-msvc\x64\`, copy to **both** locations:
   - `opengl32.dll`, `libgallium_wgl.dll`, `dxil.dll`
   - A: `...\Steam\steamapps\common\ProjectZomboid\`
   - B: `...\Steam\steamapps\common\ProjectZomboid\jre64\bin\`
4. Create two empty files:
   - A: `ProjectZomboid64.exe.local`
   - B: `java.exe.local`
5. In the launcher, turn VK ON and launch
6. Verify: launcher log shows Mesa restored, and `%UserProfile%\Zomboid\console.txt`
   shows `GraphicsCard: Mesa zink ...`

Notes: if it does not work, delete the files copied in step 3 and the `.local`
files created in step 4 (restores stock state). Environment-specific problems
cannot be supported here — this tool's authors are not the Mesa/Magpie developers.

The launcher's VK switch exists so you can change how the game starts without
the tedious manual remove/install procedure when the game has trouble.

## Magpie (FSR)

1. Download `Magpie-v0.12.1-x64.zip`:
   `https://github.com/Blinue/Magpie/releases/download/v0.12.1/Magpie-v0.12.1-x64.zip`
2. Extract anywhere (e.g. `D:\Tools\Magpie`) and run `Magpie.exe` (no install)
3. Create a profile for the Project Zomboid window (FSR mode, auto-scale ON)
4. Run the game windowed at low resolution, then scale with `Win+Shift+A` or auto-scale

Magpie settings: see the Magpie GitHub page (`https://github.com/Blinue/Magpie`).
The launcher's Magpie controls (start/kill, resident included) exist to disable
scaling when unwanted or to avoid affecting other games. If always-resident use
is fine, you don't need them.

---

These external tools are not required, nor particularly recommended.
They exist only because the author personally uses them and they fit the
launcher's goal of simplifying startup. Vulkan and FSR depend on individual
PC environments — no usage guarantee.
