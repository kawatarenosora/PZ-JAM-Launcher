# External tools setup (Zink / Magpie)

> This is one successful case, not a recommendation or support.
> No warranty.

## Zink (Mesa)

Tested working with `mesa3d-26.2.4-release-msvc` (one environment, no warranty).

1. Download `mesa3d-26.2.4-release-msvc.7z`:
   `https://github.com/pal1000/mesa-dist-win/releases/download/26.2.4/mesa3d-26.2.4-release-msvc.7z`
2. Extract it
3. From `mesa3d-26.2.4-release-msvc\x64\`, copy to **both** locations:
   - `opengl32.dll`, `libgallium_wgl.dll`, `dxil.dll`
   - A: `...\Steam\steamapps\common\ProjectZomboid\`
   - B: `...\Steam\steamapps\common\ProjectZomboid\jre64\bin\`
4. Create two empty files:
   - A: `ProjectZomboid64.exe.local`
   - B: `java.exe.local`
5. In the launcher, set the renderer to **VK (Zink)** and launch
6. Verify: launcher log shows Mesa restored, and `%UserProfile%\Zomboid\console.txt`
   shows `GraphicsCard: Mesa zink ...`

Notes: if it does not work, delete the files copied in step 3 and the `.local`
files created in step 4 (restores stock state). Environment-specific problems
cannot be supported here — this tool's authors are not the Mesa/Magpie developers.

The launcher's renderer switch (Off / VK (Zink) / D3D12) exists so you can change how
the game starts without the tedious manual remove/install procedure when the game
has trouble.

### D3D12 mode (not recommended for play)

The renderer switch also offers `D3D12` (Mesa GL over D3D12 / Dozen, launched with
`GALLIUM_DRIVER=d3d12`). **In PZ this mode currently shows display glitches, so do not
use it for play — stay on `VK (Zink)`.** It is kept as a check tool only: after a Mesa
update, try D3D12 once to see whether the bugs have been fixed in the new build.

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
