'use client';

import { motion } from 'framer-motion';
import { Check, X } from 'lucide-react';

export default function ComparisonTable() {
  const comparisonData = [
    {
      feature: 'Scan Speed',
      ttt: 'Ultra Fast (Optimized C#)',
      traditional: 'Varies (Often clunky)',
    },
    {
      feature: 'Pointer Mapping',
      ttt: 'Deep multi-level support',
      traditional: 'Basic or Paid add-on',
    },
    { feature: 'UI/UX', ttt: 'Modern Dark Mode / Cyberpunk', traditional: 'Outdated Win32 forms' },
    { feature: 'Open Source', ttt: 'Yes', traditional: 'Rarely' },
    { feature: 'Global Hotkeys', ttt: 'Built-in / Seamless', traditional: 'Requires setup' },
  ];

  return (
    <section className="py-24 relative z-10 bg-background">
      <div className="container mx-auto px-6 max-w-5xl">
        <div className="text-center mb-16">
          <h2 className="text-3xl md:text-5xl font-bold font-orbitron text-white mb-4">
            The New <span className="text-white text-glow-primary">Standard</span>
          </h2>
          <p className="text-white/60">How TTT stacks up against the legacy alternatives.</p>
        </div>

        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          className="w-full overflow-x-auto rounded-xl glass-panel"
        >
          <table className="w-full text-left border-collapse">
            <thead>
              <tr>
                <th className="p-6 border-b border-white/10 font-bold text-white/50 uppercase tracking-wider text-sm">
                  Features
                </th>
                <th className="p-6 border-b border-white/10 font-bold text-primary text-xl font-orbitron bg-primary/5">
                  TTT
                </th>
                <th className="p-6 border-b border-white/10 font-bold text-white/50 uppercase tracking-wider text-sm">
                  Traditional Tools
                </th>
              </tr>
            </thead>
            <tbody>
              {comparisonData.map((row, idx) => (
                <tr
                  key={idx}
                  className="hover:bg-white/5 transition-colors"
                >
                  <td className="p-6 border-b border-white/5 font-medium text-white/80">
                    {row.feature}
                  </td>
                  <td className="p-6 border-b border-white/5 bg-primary/5 text-white">
                    <div className="flex items-center gap-2">
                      <Check
                        size={18}
                        className="text-primary"
                      />
                      {row.ttt}
                    </div>
                  </td>
                  <td className="p-6 border-b border-white/5 text-white/50">
                    <div className="flex items-center gap-2">
                      <X
                        size={18}
                        className="text-red-500/50"
                      />
                      {row.traditional}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </motion.div>
      </div>
    </section>
  );
}
