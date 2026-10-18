export type IconName = 'home' | 'grid' | 'requests' | 'plus' | 'arrow' | 'chevron' | 'clock' | 'check' | 'help' | 'logout' | 'pin' | 'calendar' | 'tool' | 'menu' | 'close' | 'shield';

const paths: Record<IconName, string> = {
  home: 'M3 10 12 3l9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z',
  grid: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
  requests: 'M8 4H5a1 1 0 0 0-1 1v15a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1V5a1 1 0 0 0-1-1h-3 M8 2h8v5H8z M8 12h8 M8 16h5',
  plus: 'M12 5v14 M5 12h14',
  arrow: 'M4 12h16 M14 6l6 6-6 6',
  chevron: 'M9 5l7 7-7 7',
  clock: 'M12 8v5l3 2 M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',
  check: 'M5 12l4 4L19 6',
  help: 'M9.2 8a3 3 0 0 1 5.6 2c-.8 1.4-2.8 1.5-2.8 3 M12 17h.01 M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',
  logout: 'M10 4H4v16h6 M13 8l4 4-4 4 M8 12h13',
  pin: 'M20 10c0 6-8 12-8 12S4 16 4 10a8 8 0 1 1 16 0 M15 10a3 3 0 1 1-6 0 3 3 0 0 1 6 0',
  calendar: 'M4 5h16v16H4z M4 10h16 M8 2v5 M16 2v5',
  tool: 'm14 6 4 4 M20.5 3.5a6 6 0 0 1-7.8 7.8l-7.5 7.5a2 2 0 0 1-2.8-2.8l7.5-7.5a6 6 0 0 1 7.8-7.8L14 4.4 17.6 8Z',
  menu: 'M4 6h16 M4 12h16 M4 18h16',
  close: 'm6 6 12 12 M18 6 6 18',
  shield: 'M12 2 3 6v6c0 5 9 10 9 10s9-5 9-10V6Z M8 12l3 3 5-6',
};

export function Icon({ name, className = '' }: { name: IconName; className?: string }) {
  return <svg className={`icon ${className}`} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={paths[name]} /></svg>;
}

export function BuildingIllustration() {
  return <svg className="building-art" viewBox="0 0 440 320" fill="none" aria-hidden="true">
    <circle cx="328" cy="81" r="46" fill="#d9d5b9" />
    <path d="M0 287h440" stroke="#c8c9b6" />
    <path d="m118 110 110-41 101 38v179H118Z" fill="#ded9c8" />
    <path d="m118 110 110-41v217H118Z" fill="#e9e3d4" />
    <path d="m228 69 101 38v179H228Z" fill="#c6c9b6" />
    <path d="M96 120 228 68l122 45" stroke="#465b4b" strokeWidth="5" strokeLinecap="square" />
    <path d="M103 139h126v147H103Z" fill="#e8deca" />
    <path d="M99 140h134" stroke="#798373" strokeWidth="6" />
    {[0, 1, 2].map(row => <g key={row}>{[0, 1].map(col => <g key={col}>
      <path d={`M${132 + col * 52} ${121 + row * 49}h28v35h-28Z`} fill="#536f66" />
      <path d={`M${146 + col * 52} ${121 + row * 49}v35 m-14-18h28`} stroke="#dce1ce" strokeWidth="2" />
      <path d={`M${128 + col * 52} ${157 + row * 49}h36`} stroke="#b6b5a2" strokeWidth="3" />
    </g>)}</g>)}
    {[0, 1, 2].map(row => <g key={row}>
      <path d={`m252 ${122 + row * 49} 27 7v31l-27-6Z m42 11 20 6v29l-20-5Z`} fill="#536f66" />
      <path d={`m265 ${125 + row * 49}v32 m39-20v29`} stroke="#bdcbb9" strokeWidth="2" />
    </g>)}
    <path d="M158 249h29v37h-29Z" fill="#3e5a4d" /><path d="M173 249v37" stroke="#b8c8ac" />
    <path d="M75 287v-50 m-22 0h44" stroke="#7e8f6c" strokeWidth="3" />
    <ellipse cx="75" cy="218" rx="23" ry="37" fill="#839777" />
    <ellipse cx="346" cy="252" rx="35" ry="24" fill="#9aa885" />
    <ellipse cx="320" cy="270" rx="27" ry="15" fill="#7b9073" />
    <path d="M146 294h58" stroke="#b9bca6" strokeWidth="7" />
  </svg>;
}
