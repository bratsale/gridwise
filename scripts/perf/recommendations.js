import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';

// Server-Timing phases from the service, in ms.
const phases = {
  db_read: new Trend('db_read', true),
  calc: new Trend('calc', true),
  json: new Trend('json', true),
  save: new Trend('save', true),
};
const SERVER_TIMING = /(db-read|calc|json|save);dur=([0-9.]+)/g;

// SCENARIO selects the load (see docs/perf/README.md):
//   target (default): 50 iterations/s for 60 s after a 10 s warm-up at 10/s; latency and dropped iterations are checked.
//   single: 1 VU for 30 s, the baseline without contention.
//   stress: closed loop with 10 VUs for 60 s, no latency threshold; it shows the maximum throughput.
const SCENARIO = __ENV.SCENARIO || 'target';

const warmupArrival = {
  executor: 'constant-arrival-rate', rate: 10, timeUnit: '1s', duration: '10s',
  preAllocatedVUs: 20, maxVUs: 50, exec: 'run', gracefulStop: '0s',
};

const definitions = {
  target: {
    scenarios: {
      warmup: warmupArrival,
      target: {
        executor: 'constant-arrival-rate', rate: 50, timeUnit: '1s', duration: '60s', startTime: '10s',
        preAllocatedVUs: 20, maxVUs: 50, exec: 'run',
      },
    },
    thresholds: {
      'http_req_duration{scenario:target}': ['p(95)<50'],
      'http_req_failed{scenario:target}': ['rate<0.01'],
      // The system could not keep up with the rate.
      'dropped_iterations{scenario:target}': ['count==0'],
    },
  },
  single: {
    scenarios: { single: { executor: 'constant-vus', vus: 1, duration: '30s', exec: 'run' } },
    thresholds: {
      'http_req_duration{scenario:single}': ['p(95)<50'],
      'http_req_failed{scenario:single}': ['rate<0.01'],
    },
  },
  stress: {
    scenarios: {
      warmup: { executor: 'constant-vus', vus: 2, duration: '10s', exec: 'run', gracefulStop: '0s' },
      stress: { executor: 'constant-vus', vus: 10, duration: '60s', startTime: '10s', exec: 'run' },
    },
    thresholds: {
      'http_req_failed{scenario:stress}': ['rate<0.01'],
    },
  },
};

const definition = definitions[SCENARIO];
if (!definition) throw new Error(`Unknown SCENARIO "${SCENARIO}". Use target, single or stress.`);

export const options = {
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: definition.scenarios,
  thresholds: definition.thresholds,
};

const URL = __ENV.BASE_URL || 'http://localhost:5102';
const HEADERS = { headers: { 'Content-Type': 'application/json' } };

export function run() {
  const body = JSON.stringify({
    consumptionKwh: Math.round((50 + Math.random() * 950) * 1000) / 1000,
    taxGroup: Math.random() < 0.5 ? 'household' : 'business',
  });
  const res = http.post(`${URL}/recommendations`, body, HEADERS);
  check(res, { 'status is 201': (r) => r.status === 201 });

  const timing = res.headers['Server-Timing'] || '';
  for (const m of timing.matchAll(SERVER_TIMING)) {
    phases[m[1].replace('-', '_')].add(parseFloat(m[2]));
  }
}
