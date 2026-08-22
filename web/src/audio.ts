export type SfxName =
  | "click"
  | "error"
  | "build"
  | "complete"
  | "collect"
  | "recruit"
  | "march"
  | "scout"
  | "transport"
  | "attack"
  | "mail"
  | "claim";

export type AmbientName = "none" | "login" | "city" | "outer" | "map" | "shop" | "army" | "wall";

const MUTE_KEY = "sanguo.audio.muted";

type AmbientHandles = {
  stop: () => void;
};

class GameAudio {
  private ctx: AudioContext | null = null;
  private master: GainNode | null = null;
  private music: GainNode | null = null;
  private sfx: GainNode | null = null;
  private unlocked = false;
  private ambient: AmbientName = "none";
  private handles: AmbientHandles | null = null;
  muted = readMuted();

  unlock(): void {
    const ctx = this.ensure();
    if (ctx.state === "suspended") {
      void ctx.resume();
    }
    const first = !this.unlocked;
    this.unlocked = true;
    if (this.master) {
      this.master.gain.value = this.muted ? 0 : 1;
    }
    if (first && !this.muted) {
      this.startAmbient(this.ambient);
    }
  }

  setMuted(muted: boolean): void {
    this.muted = muted;
    writeMuted(muted);
    if (this.master) {
      this.master.gain.value = muted ? 0 : 1;
    }
    if (muted) {
      this.handles?.stop();
      this.handles = null;
    } else if (this.unlocked) {
      this.startAmbient(this.ambient);
    }
  }

  toggleMuted(): boolean {
    this.setMuted(!this.muted);
    return this.muted;
  }

  setAmbient(name: AmbientName): void {
    if (this.ambient === name && this.handles) {
      return;
    }
    this.ambient = name;
    if (this.unlocked) {
      this.startAmbient(name);
    }
  }

  play(name: SfxName): void {
    if (this.muted || !this.unlocked) {
      return;
    }
    const ctx = this.ensure();
    if (ctx.state === "suspended") {
      return;
    }
    const dest = this.sfx ?? ctx.destination;
    const now = ctx.currentTime;
    switch (name) {
      case "click":
        blip(ctx, dest, now, 620, 0.05, 0.07);
        break;
      case "error":
        blip(ctx, dest, now, 140, 0.16, 0.09, "square");
        break;
      case "build":
        noiseHit(ctx, dest, now, 0.08, 0.12, 900);
        blip(ctx, dest, now + 0.04, 180, 0.08, 0.06, "triangle");
        break;
      case "complete":
        gong(ctx, dest, now);
        break;
      case "collect":
        blip(ctx, dest, now, 740, 0.08, 0.06);
        blip(ctx, dest, now + 0.07, 980, 0.1, 0.055);
        blip(ctx, dest, now + 0.14, 1240, 0.12, 0.05);
        break;
      case "recruit":
        drum(ctx, dest, now);
        drum(ctx, dest, now + 0.16);
        break;
      case "march":
        horn(ctx, dest, now, 196, 0.55);
        horn(ctx, dest, now + 0.08, 294, 0.5);
        break;
      case "scout":
        blip(ctx, dest, now, 880, 0.12, 0.05, "triangle");
        blip(ctx, dest, now + 0.1, 1174, 0.16, 0.045, "triangle");
        break;
      case "transport":
        noiseHit(ctx, dest, now, 0.18, 0.07, 420);
        blip(ctx, dest, now + 0.05, 220, 0.12, 0.05, "triangle");
        break;
      case "attack":
        noiseHit(ctx, dest, now, 0.22, 0.16, 700);
        blip(ctx, dest, now, 90, 0.22, 0.12, "sawtooth");
        break;
      case "mail":
        blip(ctx, dest, now, 784, 0.14, 0.05, "sine");
        blip(ctx, dest, now + 0.12, 988, 0.18, 0.045, "sine");
        break;
      case "claim":
        blip(ctx, dest, now, 523, 0.1, 0.06);
        blip(ctx, dest, now + 0.08, 659, 0.12, 0.055);
        blip(ctx, dest, now + 0.16, 784, 0.16, 0.05);
        break;
    }
  }

  private ensure(): AudioContext {
    if (!this.ctx) {
      const ctx = new AudioContext();
      const master = ctx.createGain();
      const music = ctx.createGain();
      const sfx = ctx.createGain();
      master.gain.value = this.muted ? 0 : 1;
      music.gain.value = 0.22;
      sfx.gain.value = 0.34;
      music.connect(master);
      sfx.connect(master);
      master.connect(ctx.destination);
      this.ctx = ctx;
      this.master = master;
      this.music = music;
      this.sfx = sfx;
    }
    return this.ctx;
  }

  private startAmbient(name: AmbientName): void {
    this.handles?.stop();
    this.handles = null;
    if (name === "none" || this.muted) {
      return;
    }
    const ctx = this.ensure();
    const dest = this.music ?? ctx.destination;
    if (name === "login") {
      this.handles = padLoop(ctx, dest, [196, 247, 294], 0.035);
      return;
    }
    if (name === "city") {
      const pad = padLoop(ctx, dest, [220, 262, 330, 392], 0.03);
      const crowd = wind(ctx, dest, 0.018, 280);
      const birds = chirpLoop(ctx, dest, 4.8);
      this.handles = join(pad, crowd, birds);
      return;
    }
    if (name === "outer") {
      const pad = padLoop(ctx, dest, [174, 220, 261], 0.028);
      const breeze = wind(ctx, dest, 0.03, 420);
      this.handles = join(pad, breeze);
      return;
    }
    if (name === "shop") {
      const pad = padLoop(ctx, dest, [262, 330, 392, 440], 0.022);
      const murmur = wind(ctx, dest, 0.022, 640);
      const chatter = chirpLoop(ctx, dest, 3.2);
      this.handles = join(pad, murmur, chatter);
      return;
    }
    if (name === "army") {
      const pad = padLoop(ctx, dest, [146, 196, 220], 0.03);
      const dust = wind(ctx, dest, 0.02, 260);
      const drums = distantDrum(ctx, dest, 2800);
      this.handles = join(pad, dust, drums);
      return;
    }
    if (name === "wall") {
      const pad = padLoop(ctx, dest, [130, 164, 196], 0.028);
      const gale = wind(ctx, dest, 0.032, 200);
      const drums = distantDrum(ctx, dest, 6400);
      this.handles = join(pad, gale, drums);
      return;
    }
    const pad = padLoop(ctx, dest, [146, 196, 246, 164], 0.026);
    const gale = wind(ctx, dest, 0.036, 180);
    const drums = distantDrum(ctx, dest);
    this.handles = join(pad, gale, drums);
  }
}

function readMuted(): boolean {
  try {
    return localStorage.getItem(MUTE_KEY) === "1";
  } catch {
    return false;
  }
}

function writeMuted(muted: boolean): void {
  try {
    localStorage.setItem(MUTE_KEY, muted ? "1" : "0");
  } catch {
    // ignore
  }
}

function blip(
  ctx: AudioContext,
  dest: AudioNode,
  when: number,
  freq: number,
  dur: number,
  gain: number,
  type: OscillatorType = "sine"
): void {
  const osc = ctx.createOscillator();
  const g = ctx.createGain();
  osc.type = type;
  osc.frequency.setValueAtTime(freq, when);
  osc.frequency.exponentialRampToValueAtTime(Math.max(40, freq * 0.72), when + dur);
  g.gain.setValueAtTime(gain, when);
  g.gain.exponentialRampToValueAtTime(0.0001, when + dur);
  osc.connect(g);
  g.connect(dest);
  osc.start(when);
  osc.stop(when + dur + 0.02);
}

function noiseHit(
  ctx: AudioContext,
  dest: AudioNode,
  when: number,
  dur: number,
  gain: number,
  cutoff: number
): void {
  const buffer = ctx.createBuffer(1, Math.floor(ctx.sampleRate * dur), ctx.sampleRate);
  const data = buffer.getChannelData(0);
  for (let i = 0; i < data.length; i++) {
    data[i] = (Math.random() * 2 - 1) * (1 - i / data.length);
  }
  const src = ctx.createBufferSource();
  src.buffer = buffer;
  const filter = ctx.createBiquadFilter();
  filter.type = "bandpass";
  filter.frequency.value = cutoff;
  const g = ctx.createGain();
  g.gain.setValueAtTime(gain, when);
  g.gain.exponentialRampToValueAtTime(0.0001, when + dur);
  src.connect(filter);
  filter.connect(g);
  g.connect(dest);
  src.start(when);
  src.stop(when + dur + 0.02);
}

function gong(ctx: AudioContext, dest: AudioNode, when: number): void {
  for (const [freq, gain, dur] of [
    [164, 0.14, 1.4],
    [246, 0.08, 1.1],
    [492, 0.04, 0.7]
  ] as const) {
    const osc = ctx.createOscillator();
    const g = ctx.createGain();
    osc.type = "sine";
    osc.frequency.setValueAtTime(freq, when);
    g.gain.setValueAtTime(gain, when);
    g.gain.exponentialRampToValueAtTime(0.0001, when + dur);
    osc.connect(g);
    g.connect(dest);
    osc.start(when);
    osc.stop(when + dur + 0.05);
  }
}

function horn(ctx: AudioContext, dest: AudioNode, when: number, freq: number, dur: number): void {
  const osc = ctx.createOscillator();
  const filter = ctx.createBiquadFilter();
  const g = ctx.createGain();
  osc.type = "sawtooth";
  osc.frequency.setValueAtTime(freq, when);
  filter.type = "lowpass";
  filter.frequency.setValueAtTime(680, when);
  g.gain.setValueAtTime(0.0001, when);
  g.gain.exponentialRampToValueAtTime(0.07, when + 0.05);
  g.gain.exponentialRampToValueAtTime(0.0001, when + dur);
  osc.connect(filter);
  filter.connect(g);
  g.connect(dest);
  osc.start(when);
  osc.stop(when + dur + 0.02);
}

function drum(ctx: AudioContext, dest: AudioNode, when: number): void {
  const osc = ctx.createOscillator();
  const g = ctx.createGain();
  osc.type = "sine";
  osc.frequency.setValueAtTime(140, when);
  osc.frequency.exponentialRampToValueAtTime(48, when + 0.18);
  g.gain.setValueAtTime(0.16, when);
  g.gain.exponentialRampToValueAtTime(0.0001, when + 0.2);
  osc.connect(g);
  g.connect(dest);
  osc.start(when);
  osc.stop(when + 0.22);
  noiseHit(ctx, dest, when, 0.06, 0.08, 300);
}

function padLoop(ctx: AudioContext, dest: AudioNode, notes: number[], gain: number): AmbientHandles {
  const oscs: OscillatorNode[] = [];
  const timers: number[] = [];
  let alive = true;
  notes.forEach((freq, index) => {
    const osc = ctx.createOscillator();
    const g = ctx.createGain();
    osc.type = "triangle";
    osc.frequency.value = freq;
    g.gain.value = 0.0001;
    osc.connect(g);
    g.connect(dest);
    osc.start();
    oscs.push(osc);
    const pulse = () => {
      if (!alive) {
        return;
      }
      const now = ctx.currentTime;
      g.gain.cancelScheduledValues(now);
      g.gain.setValueAtTime(0.0001, now);
      g.gain.linearRampToValueAtTime(gain, now + 0.4);
      g.gain.linearRampToValueAtTime(0.0001, now + 2.4);
    };
    timers.push(window.setTimeout(pulse, index * 420));
    timers.push(window.setInterval(pulse, 3200 + index * 180));
  });
  return {
    stop: () => {
      alive = false;
      timers.forEach((id) => {
        window.clearTimeout(id);
        window.clearInterval(id);
      });
      oscs.forEach((osc) => safeStop(osc));
    }
  };
}

function wind(ctx: AudioContext, dest: AudioNode, gain: number, cutoff: number): AmbientHandles {
  const buffer = ctx.createBuffer(1, ctx.sampleRate * 2, ctx.sampleRate);
  const data = buffer.getChannelData(0);
  for (let i = 0; i < data.length; i++) {
    data[i] = Math.random() * 2 - 1;
  }
  const src = ctx.createBufferSource();
  src.buffer = buffer;
  src.loop = true;
  const filter = ctx.createBiquadFilter();
  filter.type = "lowpass";
  filter.frequency.value = cutoff;
  const g = ctx.createGain();
  g.gain.value = gain;
  src.connect(filter);
  filter.connect(g);
  g.connect(dest);
  src.start();
  return { stop: () => safeStop(src) };
}

function chirpLoop(ctx: AudioContext, dest: AudioNode, every: number): AmbientHandles {
  const tick = () => {
    if (ctx.state !== "running") {
      return;
    }
    const now = ctx.currentTime + 0.02;
    blip(ctx, dest, now, 1400 + Math.random() * 600, 0.08, 0.018, "sine");
    blip(ctx, dest, now + 0.07, 1800 + Math.random() * 400, 0.07, 0.014, "sine");
  };
  const first = window.setTimeout(tick, 800);
  const id = window.setInterval(tick, every * 1000);
  return {
    stop: () => {
      window.clearTimeout(first);
      window.clearInterval(id);
    }
  };
}

function distantDrum(ctx: AudioContext, dest: AudioNode, every = 5200): AmbientHandles {
  const tick = () => {
    if (ctx.state !== "running") {
      return;
    }
    drum(ctx, dest, ctx.currentTime + 0.02);
  };
  const id = window.setInterval(tick, every);
  return { stop: () => window.clearInterval(id) };
}

function safeStop(node: OscillatorNode | AudioBufferSourceNode): void {
  try {
    node.stop();
  } catch {
    // already stopped
  }
}

function join(...parts: AmbientHandles[]): AmbientHandles {
  return {
    stop: () => {
      for (const part of parts) {
        part.stop();
      }
    }
  };
}

export const gameAudio = new GameAudio();
