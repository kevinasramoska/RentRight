import { useRef, useState } from 'react';
import { Icon } from '../components/Icon';

const steps = [
  { path: '/app/demo', label: 'Your home', description: 'See the tenancy and open-request summary.' },
  { path: '/app/demo/requests', label: 'Browse requests', description: 'Compare requests at each stage of a repair.' },
  { path: '/app/demo/requests/1048', label: 'Follow a repair', description: 'Read the status timeline and property-team update.' },
  { path: '/app/demo/requests/new', label: 'Report an issue', description: 'Submit a fictional issue, then check the updated overview.' },
];

export function DemoGuide({ route, onReset }: { route: string; onReset: () => void }) {
  const [confirming, setConfirming] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const resetButton = useRef<HTMLButtonElement>(null);
  const path = route.split('?')[0]?.replace(/\/$/, '');
  const current = steps.findIndex(step => step.path === path);
  return <section className="demo-guide" aria-labelledby="demo-guide-title">
    <div className="demo-guide-heading">
      <div><p className="eyebrow">A TWO-MINUTE RESIDENT JOURNEY</p><h2 id="demo-guide-title">A home. An issue. A clear next step.</h2><p>Explore at your own pace. No account, setup, or real maintenance request.</p></div>
      <div className="demo-guide-actions"><button className="button button-primary" aria-expanded={expanded} aria-controls="demo-walkthrough" onClick={() => setExpanded(value => !value)}>{expanded ? 'Hide walkthrough' : 'Show walkthrough'}</button><button ref={resetButton} className="button button-secondary" aria-expanded={confirming} aria-controls="demo-reset" onClick={() => setConfirming(value => !value)}>Reset demo</button></div>
    </div>
    {confirming && <div id="demo-reset" className="demo-reset" role="group" aria-labelledby="demo-reset-title">
      <div><strong id="demo-reset-title">Start fresh?</strong><p>This clears simulated requests and any unfinished form, then returns to the original overview. Nothing outside this demo changes.</p></div>
      <div className="demo-reset-actions"><button className="button button-secondary" onClick={() => { setConfirming(false); resetButton.current?.focus(); }}>Keep exploring</button><button className="button button-primary" onClick={() => { setConfirming(false); setExpanded(false); onReset(); }}>Reset and start again</button></div>
    </div>}
    {expanded && <nav id="demo-walkthrough" aria-label="Demo walkthrough" onClick={() => setConfirming(false)}><ol className="demo-steps">{steps.map((step, index) => <li key={step.path}><a href={step.path + (index === 0 ? '/' : '')} aria-current={index === current ? 'step' : undefined}>
      <span className="demo-step-number" aria-hidden="true">{index + 1}</span><span><strong>{step.label}</strong><span>{step.description}</span></span>
    </a></li>)}</ol></nav>}
    <p className="demo-guide-note"><Icon name="shield" />Synthetic resident scenario · changes stay in memory and disappear on reload.</p>
  </section>;
}
