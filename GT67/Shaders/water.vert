layout(location = 0) in vec3 aPosition;   // unit grid (0..1 on x and z); the skirts hang down to y = -1

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uViewPos;

// FFT ocean (OceanWaves): a tiling patch of PatchSize metres. rgb = height, sideways displacement x, z; a = foam.
uniform sampler2D uOceanDisplacement;
uniform float uPatchSize;
uniform float uOceanTexel;   // metres per texel of the patch
uniform float uWaveHeight;

// Level of detail of this tile: grid cells per side, their size in metres, and the distances over which the odd grid
// vertices slide onto the grid of the next larger tile (so the switch to that tile shows no jump).
uniform float uGridCells;
uniform vec2 uCellSize;
uniform vec2 uMorph;         // x = start, y = end (metres from the camera)

out vec3 vWorldPos;
out vec2 vBaseXZ;            // undisplaced position: the fragment shader samples the waves there per pixel
out vec4 vClip;
out float vWaveHeight;       // about -1 (trough) .. 1 (crest)

// The patch is sampled twice: as is (short waves) and 2.7x larger, turned by 34 degrees (the long waves). The turn
// and the odd scale keep the tiling from ever showing. Must match Water.CascadeScale / CascadeAngle / detail weight.
const float CASCADE_SCALE = 0.37;
const mat2 CASCADE_TURN = mat2(0.825336, 0.564642, -0.564642, 0.825336);
const float DETAIL_WEIGHT = 0.2;   // the short-wave copy is lower, as shorter waves are on real water

vec4 sampleWaves(vec2 xz, float spacing)
{
    // Waves shorter than a few grid cells cannot be shown by this mesh (they would make jagged crests): the mip level
    // matching the cell size averages them away.
    vec2 large = CASCADE_TURN * (xz * CASCADE_SCALE) + 0.5 * uPatchSize;
    float lodLarge = max(log2(spacing * CASCADE_SCALE * 2.0 / uOceanTexel), 0.0);
    float lodDetail = max(log2(spacing * 2.0 / uOceanTexel), 0.0);
    vec4 a = textureLod(uOceanDisplacement, large / uPatchSize, lodLarge);
    vec4 b = textureLod(uOceanDisplacement, xz / uPatchSize, lodDetail);
    // Sideways displacement of the turned copy is turned back into world directions.
    vec2 sideways = transpose(CASCADE_TURN) * a.gb + b.gb * DETAIL_WEIGHT;
    return vec4(a.r + b.r * DETAIL_WEIGHT, sideways, 0.0);
}

void main()
{
    // Geomorphing: as the vertex nears the distance where the larger tile takes over, every odd grid vertex glides
    // onto its even neighbour; at the end of the range this tile's grid is exactly the larger tile's grid.
    vec3 local = aPosition;
    vec3 start = (uModel * vec4(local, 1.0)).xyz;
    float morph = clamp((distance(start, uViewPos) - uMorph.x) / max(uMorph.y - uMorph.x, 1e-3), 0.0, 1.0);
    vec2 index = floor(local.xz * uGridCells + 0.5);
    local.xz -= mod(index, 2.0) / uGridCells * morph;
    vec3 base = (uModel * vec4(local, 1.0)).xyz;

    float spacing = max(uCellSize.x, uCellSize.y) * (1.0 + morph);
    vec4 waves = sampleWaves(base.xz, spacing);
    vec3 position = base + vec3(waves.y, waves.x, waves.z);

    vWorldPos = position;
    vBaseXZ = base.xz;
    vWaveHeight = waves.x / (uWaveHeight * 1.2);
    vClip = uProjection * uView * vec4(position, 1.0);
    gl_Position = vClip;
}
