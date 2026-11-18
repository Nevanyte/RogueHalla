# rh-alloc-count: a counting allocator for allocation tests

A tiny test-only crate. It provides `CountingAlloc`, a global allocator that forwards every request to the system
allocator and counts the blocks requested by the **current thread**. A test uses it to prove that a hot path (for
example a terrain query) allocates nothing once it is warm.

It is a dev-dependency only: no shipped crate may depend on it normally. It has no dependencies of its own, and it is the
one crate in the workspace that allows `unsafe` (an allocator is `unsafe impl` by definition; it only forwards to
`System`).

## Where it sits

`rh-sim` lists it under `[dev-dependencies]` and uses it in its allocation tests (`crates/rh-sim/tests/terrain_no_alloc.rs`,
with `alloc_count_support.rs`). Nothing calls it at run time.

## Entry points

- `src/lib.rs`: the whole crate. `CountingAlloc` (the allocator) and `blocks_on_this_thread(f)`, which runs `f` and
  returns its result with the number of blocks the thread requested meanwhile. The file header has the step-by-step
  recipe for writing an allocation test.

## How to use it

In the test binary, install the allocator, warm up outside the measured section, then measure the steady section:

```rust
#[global_allocator]
static ALLOC: rh_alloc_count::CountingAlloc = rh_alloc_count::CountingAlloc;

let (_, blocks) = rh_alloc_count::blocks_on_this_thread(|| steady_section());
assert_eq!(blocks, 0);
```

The count is per thread, so tests need no lock and may run in parallel.

## Tests

```sh
cargo test -p rh-alloc-count     # the crate has no tests of its own
cargo test -p rh-sim             # the allocation tests that use it
```
