// npm run vectors -> regenerates spec/vectors.json. Run once and commit; test/prng.test.ts fails
// if the committed file and the current code disagree.

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { buildVectors, formatVectors } from '../src/vectors.ts';

const out = fileURLToPath(new URL('../../spec/vectors.json', import.meta.url));
writeFileSync(out, formatVectors(buildVectors()));
console.log(`wrote ${out}`);
