const LOAD_OLDER = 'LoadOlderAsync';
const DELETE_WIDTH = 80;
const DELETE_NOTIFICATION = 'DeleteNotificationAsync';
const COMPLETE_DELETE = 'CompleteNotificationDeleteAsync';
const FULL_SWIPE_FRACTION = 0.65;

class NotificationSwipes {
    #root;
    #receiver;
    #deleting = new Set();
    #removals = new Set();
    #disposed = false;
    #events = new AbortController();
    #openRow;
    #gesture;
    #animations = new Map();
    #suppressRow;
    #suppressUntil = 0;

    constructor(root, receiver) {
        this.#root = root;
        this.#receiver = receiver;
        const options = { signal: this.#events.signal };
        root.addEventListener('pointerdown', event => this.#down(event), options);
        root.addEventListener('pointermove', event => this.#move(event), options);
        root.addEventListener('pointerup', event => this.#up(event), options);
        root.addEventListener('pointercancel', event => this.#up(event, true), options);
        root.addEventListener('keydown', event => this.#key(event), options);
        root.addEventListener('click', event => {
            if (performance.now() < this.#suppressUntil && event.target.closest('[data-swipe-row]') === this.#suppressRow) {
                event.preventDefault();
                event.stopImmediatePropagation();
            }
        }, { ...options, capture: true });
    }

    #offset(row) {
        const transform = getComputedStyle(row.querySelector('.os-notification-row')).transform;
        return transform === 'none' ? 0 : new DOMMatrixReadOnly(transform).m41;
    }

    #write(row, value) {
        row.style.setProperty('--swipe-offset', `${value}px`);
        row.style.setProperty('--delete-progress', Math.min(1, Math.abs(value) / DELETE_WIDTH));
    }

    #cancelAnimation(row) {
        const animation = this.#animations.get(row);
        if (animation) { cancelAnimationFrame(animation.frame); animation.done(); }
        this.#animations.delete(row);
    }

    #settle(row, target, velocity = 0) {
        this.#cancelAnimation(row);
        row.classList.toggle('is-open', target < 0);
        if (matchMedia('(prefers-reduced-motion: reduce)').matches) {
            this.#write(row, target);
            return Promise.resolve();
        }
        return new Promise(done => {
            const animation = { position: this.#offset(row), velocity, last: performance.now(), frame: 0, done };
            const frame = now => {
                if (!row.isConnected || this.#disposed) { this.#animations.delete(row); done(); return; }
                const dt = Math.min((now - animation.last) / 1000, 0.032);
                animation.last = now;
                animation.velocity += (-520 * (animation.position - target) - 46 * animation.velocity) * dt;
                animation.position = Math.max(-row.clientWidth, Math.min(0, animation.position + animation.velocity * dt));
                this.#write(row, animation.position);
                if (Math.abs(animation.position - target) < 0.3 && Math.abs(animation.velocity) < 4) {
                    this.#write(row, target);
                    this.#animations.delete(row);
                    done();
                } else {
                    animation.frame = requestAnimationFrame(frame);
                }
            };
            this.#animations.set(row, animation);
            animation.frame = requestAnimationFrame(frame);
        });
    }

    async deleteNotification(id) {
        const row = [...this.#root.querySelectorAll('[data-swipe-row]')]
            .find(item => item.dataset.notificationId === id);
        if (row) await this.#delete(row);
    }

    async #delete(row, velocity = 0) {
        const id = row.dataset.notificationId;
        if (this.#disposed || this.#deleting.has(id)) return;
        this.#deleting.add(id);
        row.classList.add('is-deleting');
        row.setAttribute('aria-busy', 'true');
        const hadFocus = row.contains(document.activeElement);
        const visibleRows = [...this.#root.querySelectorAll('[data-swipe-row]')];
        const index = visibleRows.indexOf(row);
        const nextId = (visibleRows[index + 1] ?? visibleRows[index - 1])?.dataset.notificationId;
        try {
            const leave = this.#settle(row, -row.clientWidth, velocity);
            const success = await this.#receiver.invokeMethodAsync(DELETE_NOTIFICATION, id);
            if (!success || this.#disposed) {
                await this.#settle(row, -DELETE_WIDTH);
                return;
            }
            await leave;
            if (row.isConnected && !matchMedia('(prefers-reduced-motion: reduce)').matches) {
                const removal = row.animate([{ height: `${row.clientHeight}px`, opacity: 1 }, { height: '0px', opacity: 0 }],
                    { duration: 180, easing: 'cubic-bezier(.16,1,.3,1)', fill: 'forwards' });
                this.#removals.add(removal);
                try { await removal.finished; } finally { this.#removals.delete(removal); }
            }
            if (!this.#disposed) await this.#receiver.invokeMethodAsync(COMPLETE_DELETE, id);
            if (!this.#disposed && hadFocus) {
                const next = [...this.#root.querySelectorAll('[data-swipe-row]')]
                    .find(item => item.dataset.notificationId === nextId);
                (next ?? this.#root).focus({ preventScroll: true });
            }
            if (this.#openRow === row) this.#openRow = undefined;
        } catch {
            // Failed requests and disconnected circuits keep the row available for a later retry.
            if (!this.#disposed && row.isConnected) await this.#settle(row, -DELETE_WIDTH);
        } finally {
            this.#deleting.delete(id);
            row.classList.remove('is-deleting');
            row.removeAttribute('aria-busy');
        }
    }

    #reveal(row, reveal, velocity = 0) {
        if (this.#openRow && this.#openRow !== row) this.#settle(this.#openRow, 0);
        this.#openRow = reveal ? row : undefined;
        this.#settle(row, reveal ? -DELETE_WIDTH : 0, velocity);
    }

    #down(event) {
        if (!event.isPrimary || event.button !== 0) return;
        // A fresh press is intentional; suppress only the click synthesized by the drag itself.
        this.#suppressUntil = 0;
        this.#suppressRow = undefined;
        const row = event.target.closest('[data-swipe-row]');
        if (!row || this.#deleting.has(row.dataset.notificationId) || event.target.closest('.os-notification-delete')) return;
        if (this.#openRow && this.#openRow !== row) this.#reveal(this.#openRow, false);
        const offset = this.#offset(row);
        this.#cancelAnimation(row);
        this.#write(row, offset);
        this.#gesture = { row, pointerId: event.pointerId, x: event.clientX, y: event.clientY,
            offset, active: false, lastX: event.clientX, lastTime: performance.now(), velocity: 0 };
    }

    #move(event) {
        const gesture = this.#gesture;
        if (!gesture || gesture.pointerId !== event.pointerId) return;
        const dx = event.clientX - gesture.x;
        const dy = event.clientY - gesture.y;
        if (!gesture.active) {
            if (Math.abs(dy) > 8 && Math.abs(dy) >= Math.abs(dx)) {
                this.#settle(gesture.row, gesture.row.classList.contains('is-open') ? -DELETE_WIDTH : 0);
                this.#gesture = undefined;
                return;
            }
            if (Math.abs(dx) < 10 || Math.abs(dx) <= Math.abs(dy) * 1.15) return;
            gesture.active = true;
            this.#root.setPointerCapture(event.pointerId);
            gesture.row.classList.add('is-dragging');
        }
        const now = performance.now();
        const deltaTime = now - gesture.lastTime;
        if (deltaTime > 0) gesture.velocity = (event.clientX - gesture.lastX) / deltaTime;
        gesture.lastTime = now;
        gesture.lastX = event.clientX;
        this.#write(gesture.row, Math.max(-gesture.row.clientWidth, Math.min(0, gesture.offset + dx)));
    }

    #up(event, canceled = false) {
        const gesture = this.#gesture;
        if (!gesture || gesture.pointerId !== event.pointerId) return;
        this.#gesture = undefined;
        gesture.row.classList.remove('is-dragging');
        if (this.#root.hasPointerCapture(event.pointerId)) this.#root.releasePointerCapture(event.pointerId);
        if (!gesture.active) {
            this.#settle(gesture.row, gesture.row.classList.contains('is-open') ? -DELETE_WIDTH : 0);
            return;
        }
        this.#suppressRow = gesture.row;
        this.#suppressUntil = performance.now() + 350;
        const velocity = performance.now() - gesture.lastTime > 100 ? 0 : gesture.velocity;
        if (!canceled && velocity <= 0.35 && this.#offset(gesture.row) <= -gesture.row.clientWidth * FULL_SWIPE_FRACTION) {
            void this.#delete(gesture.row, velocity * 1000);
            return;
        }
        const reveal = canceled ? gesture.offset < -DELETE_WIDTH / 2
            : velocity < -0.35 || velocity <= 0.35 && this.#offset(gesture.row) < -DELETE_WIDTH / 2;
        this.#reveal(gesture.row, reveal, velocity * 1000);
    }

    #key(event) {
        const row = event.target.closest('[data-swipe-row]');
        if (!row || this.#deleting.has(row.dataset.notificationId)) return;
        if (event.key === 'ArrowLeft') { event.preventDefault(); this.#reveal(row, true); }
        if (event.key === 'Escape' || event.key === 'ArrowRight') {
            event.preventDefault();
            if (event.target.closest('.os-notification-delete')) row.focus();
            this.#reveal(row, false);
        }
    }

    dispose() {
        this.#disposed = true;
        this.#events.abort();
        for (const removal of this.#removals) removal.cancel();
        this.#removals.clear();
        for (const row of this.#animations.keys()) this.#cancelAnimation(row);
        this.#gesture = undefined;
    }
}

class InboxScroll {
    #root;
    #sentinel;
    #receiver;
    #observer;
    #swipes;
    #enabled = false;
    #busy = false;
    #disposed = false;
    #rowCount = -1;

    constructor(root, sentinel, receiver) {
        this.#root = root;
        this.#sentinel = sentinel;
        this.#receiver = receiver;
        this.#swipes = new NotificationSwipes(root, receiver);
        this.#observer = new IntersectionObserver(entries => {
            if (entries.some(entry => entry.isIntersecting)) this.#request();
        }, { root, rootMargin: '0px 0px 320px 0px' });
    }

    async #request() {
        if (!this.#enabled || this.#busy || this.#disposed) return;
        this.#busy = true;
        const beforeCount = this.#rowCount;
        try {
            await this.#receiver.invokeMethodAsync(LOAD_OLDER);
        } catch {
            // The page or its circuit may have been disposed during a request.
        } finally {
            this.#busy = false;
            if (!this.#disposed && beforeCount !== this.#rowCount) requestAnimationFrame(() => {
                const root = this.#root.getBoundingClientRect();
                const sentinel = this.#sentinel.getBoundingClientRect();
                if (sentinel.top < root.bottom + 320 && sentinel.bottom > root.top) this.#request();
            });
        }
    }

    async deleteNotification(id) { await this.#swipes.deleteNotification(id); }

    matches(root, sentinel) { return this.#root === root && this.#sentinel === sentinel; }

    update(enabled, rowCount) {
        if (this.#enabled === enabled && this.#rowCount === rowCount) return;
        this.#enabled = enabled;
        this.#rowCount = rowCount;
        this.#observer.unobserve(this.#sentinel);
        if (enabled) this.#observer.observe(this.#sentinel);
    }

    dispose() {
        this.#disposed = true;
        this.#enabled = false;
        this.#observer.disconnect();
        this.#swipes.dispose();
    }
}

let scroll;

export function update(root, sentinel, receiver, enabled, rowCount) {
    if (!scroll?.matches(root, sentinel)) {
        scroll?.dispose();
        scroll = new InboxScroll(root, sentinel, receiver);
    }
    scroll.update(enabled, rowCount);
}

export async function deleteNotification(id) {
    await scroll?.deleteNotification(id);
}

export function dispose() {
    scroll?.dispose();
    scroll = undefined;
}
