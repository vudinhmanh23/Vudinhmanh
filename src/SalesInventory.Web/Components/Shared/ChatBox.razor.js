// Keeps the chat box scrolled to the newest text while an answer streams in.
// "Stuck" = the user is at (or near) the bottom. Scrolling up to read an older message un-sticks it, so new text does not
// pull them back down; scrolling back to the bottom, or sending a message (force), sticks it again.
const states = new WeakMap();

function stateOf(element) {
    let state = states.get(element);
    if (!state) {
        state = { stuck: true };
        element.addEventListener('scroll', () => {
            state.stuck = element.scrollHeight - element.scrollTop - element.clientHeight < 40;
        });
        states.set(element, state);
    }
    return state;
}

export function follow(element, force) {
    if (!element) {
        return;
    }

    const state = stateOf(element);
    if (force) {
        state.stuck = true;
    }

    if (state.stuck) {
        element.scrollTop = element.scrollHeight;
    }
}
