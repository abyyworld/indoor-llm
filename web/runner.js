// Session flow: practice, eight scored trials, the review block.
//
// The same order and the same timings as the Unity TrialRunner and OversightReview, and
// reading the same generated files, so the two versions cannot drift.

import * as THREE from 'three';
import {
  ROOM, EXPOSURE_SECONDS, PRACTICE_EXPOSURE, REVIEW_EXPOSURE, TRANSITION_SECONDS,
  intensityFor, wallColour, ROUGHNESS, TEXTURE_ROUGHNESS,
  Logger, buildLinear, buildCurved, buildFurniture,
} from './study.js';

const $ = id => document.getElementById(id);
const wait = ms => new Promise(r => setTimeout(r, ms));

export class Study {
  constructor(participant, data) {
    this.participant = participant;
    this.data = data;
    this.log = new Logger(participant);
    this.responses = [];
    this.stopped = false;
  }

  // ------------------------------------------------------------------ scene setup

  init(renderer) {
    this.renderer = renderer;
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x000000);

    this.camera = new THREE.PerspectiveCamera(70, 1, 0.05, 60);

    // The rig carries the researcher-set standing position; WebXR moves the camera
    // inside it. Height and posture therefore never change where the study says the
    // participant is standing, which is what keeps the two shapes' sightlines matched.
    this.rig = new THREE.Group();
    this.rig.position.set(0, 0, ROOM.standFromEntrance);
    this.rig.add(this.camera);
    this.scene.add(this.rig);

    this.surface = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.8 });
    const materials = { surface: this.surface };

    this.linear = buildLinear(materials);
    this.curved = buildCurved(materials);
    this.furniture = buildFurniture();
    this.furnitureCurved = buildFurniture();
    this.linear.add(this.furniture);
    this.curved.add(this.furnitureCurved);
    this.linear.visible = this.curved.visible = false;
    this.scene.add(this.linear, this.curved);

    this.light = new THREE.PointLight(0xffffff, 1, 0, 1.4);
    this.light.position.set(0, ROOM.height - 0.15, ROOM.depth * 0.45);
    this.scene.add(this.light);
    this.ambient = new THREE.AmbientLight(0xffffff, 0.12);
    this.scene.add(this.ambient);

    this.buildGrid();
    this.buildPanel();
    this.setUpControllers();
  }

  setUpControllers() {
    this.pointers = [];
    for (let i = 0; i < 2; i++) {
      const controller = this.renderer.xr.getController(i);
      controller.addEventListener('selectstart', () => this.onSelect());
      this.rig.add(controller);

      const line = new THREE.Line(
        new THREE.BufferGeometry().setFromPoints(
          [new THREE.Vector3(0, 0, 0), new THREE.Vector3(0, 0, -5)]),
        new THREE.LineBasicMaterial({ color: 0x4da6ff }));
      line.visible = false;
      controller.add(line);
      controller.userData.line = line;
      this.pointers.push(controller);
    }
    this.raycaster = new THREE.Raycaster();
  }

  activePointer() {
    // Whichever controller is actually being tracked. A participant handed the other
    // controller should not silently have no pointer.
    for (const c of this.pointers) if (c.visible && c.matrixWorld) return c;
    return this.pointers[0];
  }

  // ------------------------------------------------------------- the affect grid

  buildGrid() {
    this.grid = new THREE.Group();
    this.grid.visible = false;
    this.rig.add(this.grid);

    const size = 1.1, cells = 9, step = size / cells;
    this.gridCells = [];

    for (let v = 0; v < cells; v++) {
      for (let a = 0; a < cells; a++) {
        const cell = new THREE.Mesh(
          new THREE.PlaneGeometry(step * 0.92, step * 0.92),
          new THREE.MeshBasicMaterial({ color: 0x2b2b33 }));
        cell.position.set((v - 4) * step, (a - 4) * step, -1.2);
        cell.userData = { valence: v + 1, arousal: a + 1 };
        this.grid.add(cell);
        this.gridCells.push(cell);
      }
    }

    const label = (text, x, y) => {
      const sprite = makeLabel(text);
      sprite.position.set(x, y, -1.2);
      this.grid.add(sprite);
    };
    label('unpleasant', -0.78, 0);
    label('pleasant', 0.78, 0);
    label('worked up', 0, 0.75);
    label('calm', 0, -0.75);
    this.gridTitle = makeLabel('How did that room make you feel?');
    this.gridTitle.position.set(0, 0.95, -1.2);
    this.grid.add(this.gridTitle);
  }

  // ------------------------------------------------------------ question panels

  buildPanel() {
    this.panel = new THREE.Group();
    this.panel.visible = false;
    this.rig.add(this.panel);
    this.panelButtons = [];
    this.panelTitle = makeLabel('');
    this.panelTitle.position.set(0, 0.55, -1.2);
    this.panel.add(this.panelTitle);
  }

  showPanel(question, options) {
    for (const b of this.panelButtons) this.panel.remove(b);
    this.panelButtons = [];

    setLabel(this.panelTitle, question);

    const perRow = options.length > 4 ? 3 : options.length;
    options.forEach((value, i) => {
      const col = i % perRow, row = Math.floor(i / perRow);
      const inRow = Math.min(perRow, options.length - row * perRow);

      const button = new THREE.Mesh(
        new THREE.PlaneGeometry(0.44, 0.16),
        new THREE.MeshBasicMaterial({ color: 0x2b2b33 }));
      button.position.set((col - (inRow - 1) / 2) * 0.5, 0.25 - row * 0.22, -1.2);
      button.userData = { value };
      this.panel.add(button);

      const text = makeLabel(String(value), 256, 64);
      text.position.copy(button.position);
      text.position.z += 0.005;
      text.scale.set(0.42, 0.11, 1);
      this.panel.add(text);
      this.panelButtons.push(button, text);
    });

    this.panel.visible = true;
    this.awaiting = 'panel';
    this.shownAt = performance.now();
    return new Promise(resolve => { this.resolvePanel = resolve; });
  }

  hidePanel() { this.panel.visible = false; this.awaiting = null; }

  // ------------------------------------------------------------------- interaction

  onSelect() {
    if (this.stopped) return;
    const hit = this.hoverTarget;
    if (!hit) return;
    // Ignore anything in the first 400 ms: a click aimed at the previous screen must not
    // fall through into an answer.
    if (performance.now() - this.shownAt < 400) return;

    if (this.awaiting === 'grid' && hit.userData.valence) {
      const { valence, arousal } = hit.userData;
      this.grid.visible = false;
      this.awaiting = null;
      this.log.event('rating', { valence, arousal,
        response_ms: Math.round(performance.now() - this.shownAt) });
      this.resolveGrid({ valence, arousal,
        ms: Math.round(performance.now() - this.shownAt) });
    } else if (this.awaiting === 'panel' && hit.userData.value !== undefined) {
      const value = hit.userData.value;
      this.hidePanel();
      this.log.event('panel_answer', { answer: value,
        response_ms: Math.round(performance.now() - this.shownAt) });
      this.resolvePanel(value);
    }
  }

  update() {
    const pointer = this.activePointer();
    if (pointer) {
      const origin = new THREE.Vector3();
      const direction = new THREE.Vector3(0, 0, -1);
      pointer.getWorldPosition(origin);
      direction.applyQuaternion(pointer.getWorldQuaternion(new THREE.Quaternion()));
      this.raycaster.set(origin, direction);

      const targets = this.awaiting === 'grid' ? this.gridCells
                    : this.awaiting === 'panel' ? this.panelButtons.filter(b => b.userData.value !== undefined)
                    : [];
      for (const t of targets) t.material.color.setHex(0x2b2b33);

      const hits = this.raycaster.intersectObjects(targets, false);
      this.hoverTarget = hits.length ? hits[0].object : null;
      if (this.hoverTarget) this.hoverTarget.material.color.setHex(0x4da6ff);

      for (const c of this.pointers) c.userData.line.visible = this.awaiting !== null;
    }

    const now = performance.now();
    if (!this.lastSample || now - this.lastSample > 50) {
      this.lastSample = now;
      const head = new THREE.Vector3();
      this.camera.getWorldPosition(head);
      this.log.sample({ position: head, quaternion: this.camera.quaternion },
                      this.activePointer());
    }
  }

  // -------------------------------------------------------------------- the rooms

  showRoom(config) {
    const curved = config.shape === 'curved';
    this.linear.visible = !curved;
    this.curved.visible = curved;

    this.surface.color.copy(wallColour(config));
    const base = TEXTURE_ROUGHNESS[config.texture] ?? 0.8;
    const modifier = ROUGHNESS[config.roughness] ?? 0.6;
    this.surface.roughness = Math.min(1, (base + modifier) / 2);

    this.light.intensity = intensityFor(config.brightness) * 6;
    this.log.config = config;
    this.log.event('room_shown', { rationale: config.rationale ?? '' });
  }

  hideRooms() {
    this.linear.visible = this.curved.visible = false;
    this.log.config = null;
  }

  askGrid(prompt) {
    setLabel(this.gridTitle, prompt);
    this.grid.visible = true;
    this.awaiting = 'grid';
    this.shownAt = performance.now();
    return new Promise(resolve => { this.resolveGrid = resolve; });
  }

  // --------------------------------------------------------------------- the flow

  async run() {
    this.log.event('session_begin');

    const practice = this.data.practice?.rooms ?? [];
    this.log.phase = 'practice';
    for (let i = 0; i < practice.length && !this.stopped; i++) {
      await this.trial(practice[i], -(i + 1), PRACTICE_EXPOSURE, true);
    }

    this.log.phase = 'A';
    const rooms = this.data.session.rooms;
    for (let i = 0; i < rooms.length && !this.stopped; i++) {
      await this.trial(rooms[i], i + 1, EXPOSURE_SECONDS, false);
    }

    this.log.phase = 'B';
    const trials = this.data.oversight?.trials ?? [];
    for (let i = 0; i < trials.length && !this.stopped; i++) {
      await this.reviewTrial(trials[i], i + 1);
    }

    this.log.phase = 'done';
    this.log.event('session_complete');
    await this.log.flush();
    await this.finish();
  }

  async trial(config, index, exposure, isPractice) {
    this.log.trialIndex = index;
    this.log.trialId = config.id;
    this.log.segment = 'exposure';
    this.log.event('trial_start', { practice: isPractice ? 1 : 0 });

    this.showRoom(config);
    await wait(exposure * 1000);
    if (this.stopped) return;

    this.hideRooms();
    this.log.segment = 'rating';
    const rating = await this.askGrid('How did that room make you feel?');
    if (this.stopped) return;

    if (!isPractice) {
      this.responses.push({
        participant: this.participant, source: 'trial',
        trial_index: index, trial_id: config.id,
        target_emotion: config.target_emotion, shape: config.shape,
        hue: config.hue, saturation: config.saturation,
        brightness: config.brightness, texture: config.texture,
        roughness: config.roughness,
        valence: rating.valence, arousal: rating.arousal,
        response_ms: rating.ms, exposure_ms: exposure * 1000,
        utc: new Date().toISOString(), utc_ms: Date.now(),
      });
      await this.postResponses();
    }

    this.log.segment = 'transition';
    this.log.event('trial_end');
    await wait(TRANSITION_SECONDS * 1000);
  }

  async reviewTrial(trial, index) {
    const config = trial.stimulus;
    this.log.trialIndex = index;
    this.log.trialId = trial.trial_id;
    this.log.segment = 'review';
    this.log.event('review_trial_start', {
      condition: trial.condition,
      swapped_field: trial.ground_truth?.swapped_field ?? '',
      shown_as: trial.target_emotion_shown,
    });

    this.showRoom(config);
    await wait(REVIEW_EXPOSURE * 1000);
    if (this.stopped) return;

    this.hideRooms();
    const before = await this.askGrid('How does this room make you feel?');
    if (this.stopped) return;

    this.showRoom(config);
    const noticed = await this.showPanel(
      `This room was built to feel ${trial.target_emotion_shown}.\nDoes anything look wrong for that?`,
      ['no', 'yes']);
    if (this.stopped) return;

    let field = '', value = '';
    if (noticed === 'yes') {
      field = await this.showPanel('Which one is wrong?',
        ['hue', 'saturation', 'texture', 'roughness', 'brightness', 'nothing_wrong']);
      if (this.stopped) return;

      if (field !== 'nothing_wrong') {
        value = await this.showPanel(`What should ${field} be instead?`,
          POOL_VALUES[field] ?? []);
        if (this.stopped) return;
      } else {
        field = '';
      }
    }

    // The correction loop. Apply what they chose, show it, and let them rate the
    // room they themselves produced. This is what turns a correction from a menu
    // choice into a measurable signal: the reference is their own first rating of
    // the same room, so no external criterion is needed -- and it is what makes the
    // participant a principal rather than a rater. The Unity route has had this all
    // along; the browser route losing it would have quietly dropped the study's
    // central measure on one platform.
    // The yoked control. Half the corrected trials apply a value the participant did
    // not choose, unannounced, because otherwise the correction effect cannot be told
    // apart from self-consistency: someone rates their own fix highly because it is
    // theirs. Both values are logged; analysis compares own against yoked.
    let after = null, applied = false, appliedValue = '', source = '';
    if (field && value !== '') {
      const yoked = trial.correction_source === 'yoked';
      appliedValue = yoked ? otherValueFor(field, value) : value;
      source = yoked ? 'yoked' : 'own';

      const corrected = { ...config, [field]: coerce(field, appliedValue) };
      applied = true;
      this.log.event('correction_room_shown', {
        field, chose: String(value), applied_value: String(appliedValue), source,
      });
      this.showRoom(corrected);
      await wait(REVIEW_EXPOSURE * 1000);
      if (this.stopped) return;

      this.hideRooms();
      after = await this.askGrid('And how does the room feel now, with your change?');
      if (this.stopped) return;
    }

    this.hideRooms();
    this.responses.push({
      participant: this.participant, source: 'review',
      trial_index: index, trial_id: trial.trial_id,
      condition: trial.condition,
      target_emotion_shown: trial.target_emotion_shown,
      swapped_field: trial.ground_truth?.swapped_field ?? '',
      detected: noticed === 'yes' ? 1 : 0,
      attributed_field: field, corrected_value: value,
      applied_value: appliedValue, correction_source: source,
      valence_before: before.valence, arousal_before: before.arousal,
      valence_after: after ? after.valence : '',
      arousal_after: after ? after.arousal : '',
      correction_applied: applied ? 1 : 0,
      utc: new Date().toISOString(), utc_ms: Date.now(),
    });
    await this.postResponses();
    this.log.event('review_trial_end');
    await wait(TRANSITION_SECONDS * 1000);
  }

  async postResponses() {
    try {
      await fetch('/responses', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ participant: this.participant, rows: this.responses }),
      });
    } catch (e) { /* retried on the next trial; the rows are still held here */ }
  }

  async finish() {
    this.hideRooms();
    this.grid.visible = false;
    this.hidePanel();
    setLabel(this.gridTitle, 'Thank you. Please take the headset off.');
    this.gridTitle.position.set(0, 0, -1.5);
    this.grid.visible = true;
    await this.log.flush();
  }

  stop() {
    this.stopped = true;
    this.log.event('stopped_early');
    this.log.flush();
  }
}

// A chosen correction arrives as the button's label string; the config field it
// lands in is typed. Coercing here rather than at render time keeps the buttons dumb.
// A legal value for this field that the participant did not choose. Drawn from the same
// pool so a yoked correction is as plausible a repair as their own -- the comparison is
// about whose choice it was, not whether it was sensible.
function otherValueFor(field, chosen) {
  const values = POOL_VALUES[field];
  if (!values || values.length < 2) return chosen;
  const options = values.filter(v => String(v) !== String(chosen));
  if (!options.length) return chosen;
  return options[Math.floor(Math.random() * options.length)];
}

function coerce(field, value) {
  if (field === 'hue') return parseInt(value, 10);
  if (field === 'saturation' || field === 'brightness') return parseFloat(value);
  return String(value);
}

// Mirrors PoolConstants: the values a participant can propose as a correction.
const POOL_VALUES = {
  hue: [0, 30, 60, 90, 120, 180, 240, 270, 300, 330],
  saturation: [0.2, 0.4],
  brightness: [150, 300, 500, 750],
  texture: ['plaster', 'concrete', 'textile'],
  roughness: ['rough', 'smooth'],
};

// ---------------------------------------------------------------------- text labels

function makeLabel(text, w = 1024, h = 256) {
  const canvas = document.createElement('canvas');
  canvas.width = w; canvas.height = h;
  const sprite = new THREE.Mesh(
    new THREE.PlaneGeometry(1.0, 0.25),
    new THREE.MeshBasicMaterial({
      map: new THREE.CanvasTexture(canvas), transparent: true, depthTest: false,
    }));
  sprite.userData.canvas = canvas;
  sprite.renderOrder = 10;
  setLabel(sprite, text);
  return sprite;
}

function setLabel(sprite, text) {
  const canvas = sprite.userData.canvas;
  const ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = '#f0f0f4';
  ctx.font = `${Math.round(canvas.height / 4.5)}px -apple-system, system-ui, sans-serif`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  const lines = String(text).split('\n');
  lines.forEach((line, i) => {
    ctx.fillText(line, canvas.width / 2,
      canvas.height / 2 + (i - (lines.length - 1) / 2) * canvas.height / 3.5,
      canvas.width * 0.95);
  });
  sprite.material.map.needsUpdate = true;
}
