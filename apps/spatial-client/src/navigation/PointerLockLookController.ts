export class PointerLockLookController {
  #locked = false;
  readonly #look: (movementX: number, movementY: number) => void;

  constructor(look: (movementX: number, movementY: number) => void) {
    this.#look = look;
  }

  get locked(): boolean {
    return this.#locked;
  }

  setLocked(locked: boolean): void {
    this.#locked = locked;
  }

  move(movementX: number, movementY: number): void {
    if (this.#locked) this.#look(movementX, movementY);
  }
}
