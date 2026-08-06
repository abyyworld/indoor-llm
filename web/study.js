// The study, as a WebXR page.
//
// Why this exists: a Mac cannot drive a Quest. Quest Link is Windows-only and sideloading
// needs Developer Mode, which belongs to whoever owns the headset. The Quest's own
// browser supports WebXR, needs no permissions, no account changes and no cable, and is
// therefore the only route to VR on a borrowed headset from a Mac.
//
// It reads exactly the same generated files as the Unity build -- session.json,
// oversight.json, practice.json, questionnaires.json -- so the stimuli, the
// counterbalancing and the wording are identical and neither version can drift from the
// pipeline. The logs are written in the same shape too.
//
// Geometry mirrors RoomBuilder: 4.2 x 4.3 m linear, and a 4.2 x 2.2 m vestibule plus a
// 2.1 m semicircular apse for the curved shell, standing point 1.3 m from the entrance.

import * as THREE from 'three';

// ---------------------------------------------------------------- study constants
// Mirrors unity/Assets/Scripts/EmotionRooms/RoomBuilder.cs RoomDimensions.
const ROOM = {
  width: 4.2, depth: 4.3, height: 2.4,
  vestibule: 2.2, radius: 2.1,
  standFromEntrance: 1.3, eyeHeight: 1.6,
};

const EXPOSURE_SECONDS = 20;
const PRACTICE_EXPOSURE = 20;
const REVIEW_EXPOSURE = 8;
const TRANSITION_SECONDS = 3;

// Lux to renderer intensity. Logarithmic because perceived brightness is, so a linear
// map would make 150 and 300 lux look nearly identical and 750 blinding.
const LUX = { min: 150, max: 750, minIntensity: 0.35, maxIntensity: 2.6 };

function intensityFor(lux) {
  const t = (Math.log(Math.max(lux, 1)) - Math.log(LUX.min)) /
            (Math.log(LUX.max) - Math.log(LUX.min));
  return LUX.minIntensity + Math.min(Math.max(t, 0), 1) * (LUX.maxIntensity - LUX.minIntensity);
}

// V=100% is the documented colour spec; the renderer applies it at 0.85 albedo, which
// preserves the ratios in Mengkai's 1 Aug email.
const ALBEDO = 0.85;

function wallColour(config) {
  const colour = new THREE.Color();
  if (config.saturation <= 0.001) {
    // Achromatic rule, 1 Aug: at zero saturation the scene is black or white by value
    // and the stored hue is meaningless. Do not read it.
    colour.setRGB(ALBEDO, ALBEDO, ALBEDO);
  } else {
    colour.setHSL((config.hue % 360) / 360, config.saturation, 0.5);
    colour.multiplyScalar(ALBEDO / 0.5 * 0.5);
  }
  return colour;
}

const ROUGHNESS = { rough: 0.92, smooth: 0.25 };
const TEXTURE_ROUGHNESS = { plaster: 0.75, concrete: 0.85, textile: 0.95 };

// ------------------------------------------------------------------------- logging

class Logger {
  constructor(participant) {
    this.participant = participant;
    this.queue = [];
    this.telemetry = [];
    this.phase = 'setup';
    this.trialIndex = -1;
    this.trialId = '';
    this.segment = '';
    this.config = null;
    // Flushed on a timer as well as at the end, so a session that dies mid-way still
    // leaves everything up to that point on disk. Losing a participant to a crash is
    // bad; losing them and their data is worse.
    setInterval(() => this.flush(), 5000);
  }

  event(name, detail = {}) {
    this.queue.push({
      participant: this.participant, source: 'event', event: name,
      utc: new Date().toISOString(), utc_ms: Date.now(),
      phase: this.phase, trial_index: this.trialIndex, trial_id: this.trialId,
      segment: this.segment,
      target_emotion: this.config?.target_emotion ?? '',
      shape: this.config?.shape ?? '',
      hue: this.config?.hue ?? '', saturation: this.config?.saturation ?? '',
      brightness: this.config?.brightness ?? '', texture: this.config?.texture ?? '',
      roughness: this.config?.roughness ?? '',
      ...detail,
    });
  }

  // 20 Hz, every column every row, matching StudyTelemetry.
  sample(camera, pointer) {
    const p = camera.position, q = camera.quaternion;
    this.telemetry.push({
      participant: this.participant, source: 'telemetry',
      utc_ms: Date.now(), t: performance.now() / 1000,
      phase: this.phase, trial_index: this.trialIndex, trial_id: this.trialId,
      segment: this.segment,
      target_emotion: this.config?.target_emotion ?? '',
      shape: this.config?.shape ?? '',
      hue: this.config?.hue ?? '', saturation: this.config?.saturation ?? '',
      lux: this.config?.brightness ?? '', texture: this.config?.texture ?? '',
      roughness: this.config?.roughness ?? '',
      head_x: p.x.toFixed(4), head_y: p.y.toFixed(4), head_z: p.z.toFixed(4),
      head_qx: q.x.toFixed(4), head_qy: q.y.toFixed(4),
      head_qz: q.z.toFixed(4), head_qw: q.w.toFixed(4),
      pointer_x: pointer ? pointer.position.x.toFixed(4) : '',
      pointer_y: pointer ? pointer.position.y.toFixed(4) : '',
      pointer_z: pointer ? pointer.position.z.toFixed(4) : '',
    });
    if (this.telemetry.length > 400) this.flush();
  }

  async flush() {
    if (!this.queue.length && !this.telemetry.length) return;
    const rows = this.queue.concat(this.telemetry);
    this.queue = [];
    this.telemetry = [];
    try {
      await fetch('/log', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ participant: this.participant, rows }),
      });
    } catch (e) {
      // Put them back rather than dropping: a flaky moment on the Wi-Fi should cost a
      // retry, not a hole in the record.
      this.queue = rows.filter(r => r.source === 'event').concat(this.queue);
      this.telemetry = rows.filter(r => r.source === 'telemetry').concat(this.telemetry);
    }
  }
}

// -------------------------------------------------------------------- the room shell

function buildLinear(materials) {
  const group = new THREE.Group();
  const { width: w, depth: d, height: h } = ROOM;

  const add = (geo, pos, rot) => {
    const mesh = new THREE.Mesh(geo, materials.surface);
    mesh.position.set(...pos);
    if (rot) mesh.rotation.set(...rot);
    group.add(mesh);
  };

  add(new THREE.PlaneGeometry(w, d), [0, 0, d / 2], [-Math.PI / 2, 0, 0]);
  add(new THREE.PlaneGeometry(w, d), [0, h, d / 2], [Math.PI / 2, 0, 0]);
  add(new THREE.PlaneGeometry(w, h), [0, h / 2, d], [0, Math.PI, 0]);
  add(new THREE.PlaneGeometry(w, h), [0, h / 2, 0], [0, 0, 0]);
  add(new THREE.PlaneGeometry(d, h), [-w / 2, h / 2, d / 2], [0, Math.PI / 2, 0]);
  add(new THREE.PlaneGeometry(d, h), [w / 2, h / 2, d / 2], [0, -Math.PI / 2, 0]);
  return group;
}

function buildCurved(materials) {
  const group = new THREE.Group();
  const { width: w, height: h, vestibule: f, radius: r } = ROOM;

  const add = (geo, pos, rot) => {
    const mesh = new THREE.Mesh(geo, materials.surface);
    mesh.position.set(...pos);
    if (rot) mesh.rotation.set(...rot);
    group.add(mesh);
  };

  add(new THREE.PlaneGeometry(w, f), [0, 0, f / 2], [-Math.PI / 2, 0, 0]);
  add(new THREE.PlaneGeometry(w, f), [0, h, f / 2], [Math.PI / 2, 0, 0]);
  add(new THREE.PlaneGeometry(w, h), [0, h / 2, 0], [0, 0, 0]);
  add(new THREE.PlaneGeometry(f, h), [-w / 2, h / 2, f / 2], [0, Math.PI / 2, 0]);
  add(new THREE.PlaneGeometry(f, h), [w / 2, h / 2, f / 2], [0, -Math.PI / 2, 0]);

  // The apse: a half cylinder from the springline, with its floor and ceiling caps.
  const wall = new THREE.Mesh(
    new THREE.CylinderGeometry(r, r, h, 48, 1, true, -Math.PI / 2, Math.PI),
    materials.surface);
  wall.position.set(0, h / 2, f);
  wall.material.side = THREE.DoubleSide;
  group.add(wall);

  const cap = new THREE.CircleGeometry(r, 48, -Math.PI / 2, Math.PI);
  add(cap, [0, 0, f], [-Math.PI / 2, 0, 0]);
  add(cap.clone(), [0, h, f], [Math.PI / 2, 0, 0]);
  return group;
}

// Fixed furnishing, identical in both shells and never tinted. Neutral greys only: hue
// and saturation are the manipulation, so furniture with a colour of its own would
// compete with the thing being measured.
function buildFurniture() {
  const group = new THREE.Group();
  const d = ROOM.depth;
  const grey = v => new THREE.MeshStandardMaterial({ color: new THREE.Color(v, v, v), roughness: 0.8 });

  const box = (w, h, dp, x, y, z, shade, yaw = 0) => {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, dp), grey(shade));
    mesh.position.set(x, y, z);
    mesh.rotation.y = yaw;
    group.add(mesh);
    return mesh;
  };

  // Sofa against the far wall.
  box(2.1, 0.22, 0.85, 0, 0.32, d - 0.45, 0.34);
  box(2.1, 0.68, 0.20, 0, 0.55, d - 0.77, 0.31);
  box(0.16, 0.42, 0.85, -0.98, 0.42, d - 0.45, 0.31);
  box(0.16, 0.42, 0.85, 0.98, 0.42, d - 0.45, 0.31);

  // Armchair on the participant's left, turned toward the table.
  const chair = new THREE.Group();
  chair.position.set(-1.35, 0, d - 1.45);
  chair.rotation.y = -Math.PI / 3;
  const seat = new THREE.Mesh(new THREE.BoxGeometry(0.8, 0.22, 0.8), grey(0.40));
  seat.position.y = 0.32;
  const back = new THREE.Mesh(new THREE.BoxGeometry(0.8, 0.72, 0.18), grey(0.37));
  back.position.set(0, 0.58, -0.3);
  chair.add(seat, back);
  group.add(chair);

  // Coffee table, teacup, rug.
  box(1.1, 0.05, 0.6, 0, 0.38, d - 1.5, 0.47);
  box(0.06, 0.38, 0.06, -0.5, 0.19, d - 1.75, 0.40);
  box(0.06, 0.38, 0.06, 0.5, 0.19, d - 1.75, 0.40);
  box(0.06, 0.38, 0.06, -0.5, 0.19, d - 1.25, 0.40);
  box(0.06, 0.38, 0.06, 0.5, 0.19, d - 1.25, 0.40);
  const cup = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.035, 0.08, 16), grey(0.92));
  cup.position.set(0.18, 0.44, d - 1.5);
  group.add(cup);
  box(2.4, 0.02, 1.6, 0, 0.01, d - 1.2, 0.60);

  // Bookshelf on the participant's left wall.
  box(0.35, 1.8, 0.9, -(ROOM.width / 2 - 0.22), 0.9, d - 2.2, 0.36);

  // One picture on the far wall, one on the right wall beside the door.
  box(0.7, 0.5, 0.04, -0.3, 1.55, d - 0.06, 0.25);
  box(0.04, 0.5, 0.7, ROOM.width / 2 - 0.04, 1.55, 1.15, 0.25);

  // A door, so the room reads as a room.
  box(0.05, 2.0, 0.86, ROOM.width / 2 - 0.03, 1.0, 2.05, 0.55);
  return group;
}

export { ROOM, EXPOSURE_SECONDS, PRACTICE_EXPOSURE, REVIEW_EXPOSURE, TRANSITION_SECONDS,
         intensityFor, wallColour, ROUGHNESS, TEXTURE_ROUGHNESS,
         Logger, buildLinear, buildCurved, buildFurniture };
