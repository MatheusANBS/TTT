'use client';

import { motion } from 'framer-motion';
import { ArrowLeftRight, Code, Edit3, Keyboard, Map, Search, Target, Zap } from 'lucide-react';

const features = [
  {
    icon: <Search className="text-secondary" />,
    title: 'Advanced Memory Scanning',
    desc: 'Lightning fast initial and next scans with robust data type support.',
    placeholder: 'feature-scanner-view.mp4',
  },
  {
    icon: <Edit3 className="text-primary" />,
    title: 'Real-time Memory Editing',
    desc: 'Modify memory addresses instantly via the Address List.',
    placeholder: 'feature-address-list.png',
  },
  {
    icon: <Map className="text-accent" />,
    title: 'Pointer Mapping System',
    desc: 'Robust pointer resolving to survive dynamic memory allocation.',
    placeholder: 'feature-pointer-mapper.png',
  },
  {
    icon: <Target className="text-secondary" />,
    title: 'Process Selection',
    desc: 'Easily target any 32-bit or 64-bit running process.',
    placeholder: 'feature-process-selector.png',
  },
  {
    icon: <ArrowLeftRight className="text-primary" />,
    title: 'Value Comparison',
    desc: 'Advanced Diff/Scan algorithms to narrow down exact addresses.',
    placeholder: 'feature-diff-scan.png',
  },
  {
    icon: <Keyboard className="text-accent" />,
    title: 'Global Hotkeys',
    desc: 'Trigger scans and toggle values without leaving your game.',
    placeholder: 'feature-hotkeys-tutorial.png',
  },
];

const benefits = [
  {
    icon: (
      <Zap
        size={32}
        className="text-primary mb-4"
      />
    ),
    title: 'Built for Speed',
    desc: 'Engineered in C# with optimized loops to handle huge memory spaces effortlessly.',
  },
  {
    icon: (
      <Code
        size={32}
        className="text-secondary mb-4"
      />
    ),
    title: 'Developer Friendly',
    desc: 'Clean code structure, organized workflows, and fully open source.',
  },
  {
    icon: (
      <Target
        size={32}
        className="text-accent mb-4"
      />
    ),
    title: 'Supreme Precision',
    desc: 'Multi-level pointer maps ensure your cheats and mods always point to the right data.',
  },
];

export default function FeatureGrid() {
  return (
    <section className="py-24 relative z-10 bg-background border-t border-white/5">
      <div className="container mx-auto px-6">
        <div className="text-center mb-16">
          <h2 className="text-3xl md:text-5xl font-bold font-orbitron text-white mb-4">
            Core <span className="text-primary text-glow-primary">Arsenal</span>
          </h2>
          <p className="text-white/60 max-w-2xl mx-auto">
            Everything you need to analyze, trace, and manipulate memory on the fly.
          </p>
        </div>

        {/* Feature Grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6 mb-32">
          {features.map((feat, idx) => (
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true }}
              transition={{ delay: idx * 0.1 }}
              key={idx}
              className="glass-panel p-6 rounded-xl group hover:border-primary/50 transition-colors duration-300 relative overflow-hidden"
            >
              <div className="absolute inset-0 bg-gradient-to-br from-primary/5 to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-500" />
              <div className="w-12 h-12 rounded-lg bg-white/5 border border-white/10 flex items-center justify-center mb-4 group-hover:scale-110 transition-transform duration-300">
                {feat.icon}
              </div>
              <h3 className="text-xl font-bold text-white mb-2 font-orbitron">{feat.title}</h3>
              <p className="text-white/60 text-sm mb-4">{feat.desc}</p>

              {/* Media Placeholder for feature */}
              <div className="w-full h-32 bg-black/40 rounded border border-white/5 flex items-center justify-center overflow-hidden relative">
                <span className="text-xs font-mono text-white/30 text-center px-2">
                  [{feat.placeholder}]
                </span>
              </div>
            </motion.div>
          ))}
        </div>

        {/* Why Choose Section */}
        <div className="text-center mb-16">
          <h2 className="text-3xl md:text-5xl font-bold font-orbitron text-white mb-4">
            Why Choose <span className="text-secondary text-glow-secondary">TTT</span>
          </h2>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-12 max-w-5xl mx-auto text-center">
          {benefits.map((benefit, idx) => (
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              whileInView={{ opacity: 1, scale: 1 }}
              viewport={{ once: true }}
              transition={{ delay: idx * 0.2 }}
              key={idx}
              className="flex flex-col items-center p-6"
            >
              <div className="p-4 rounded-full bg-white/5 border border-white/10 mb-4 shadow-[0_0_30px_-10px_rgba(255,255,255,0.1)]">
                {benefit.icon}
              </div>
              <h3 className="text-2xl font-bold text-white mb-3 font-orbitron">{benefit.title}</h3>
              <p className="text-white/60 leading-relaxed">{benefit.desc}</p>
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  );
}
