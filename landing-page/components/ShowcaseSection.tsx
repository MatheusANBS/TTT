'use client';

import { motion, useScroll, useTransform } from 'framer-motion';
import { Crosshair } from 'lucide-react';
import { useRef } from 'react';

export default function ShowcaseSection() {
  const containerRef = useRef<HTMLDivElement>(null);

  const { scrollYProgress } = useScroll({
    target: containerRef,
    offset: ['start end', 'end start'],
  });

  const y1 = useTransform(scrollYProgress, [0, 1], [100, -100]);
  const y2 = useTransform(scrollYProgress, [0, 1], [-100, 100]);

  return (
    <section
      ref={containerRef}
      className="py-32 relative overflow-hidden bg-[#070a12]"
    >
      {/* Background styling */}
      <div className="absolute top-0 right-0 w-[500px] h-[500px] bg-secondary/10 rounded-full blur-[150px] pointer-events-none" />
      <div className="absolute bottom-0 left-0 w-[500px] h-[500px] bg-accent/10 rounded-full blur-[150px] pointer-events-none" />

      <div className="container mx-auto px-6 relative z-10 text-center mb-16">
        <h2 className="text-3xl md:text-5xl font-bold font-orbitron text-white mb-6">
          Uncompromised <span className="text-accent text-glow-accent">Visibility</span>
        </h2>
        <p className="text-white/60 max-w-2xl mx-auto">
          Deep-dive into process memory. See what&apos;s happening under the hood with detailed logs
          and comprehensive pointer maps.
        </p>
      </div>

      <div className="container mx-auto px-6 relative z-10">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-12 items-center">
          {/* Left Column - Logs & Info */}
          <motion.div
            style={{ y: y1 }}
            className="space-y-6"
          >
            <div className="glass-panel p-6 rounded-xl border-l-4 border-l-secondary">
              <div className="flex items-center gap-3 mb-4">
                <Crosshair className="text-secondary" />
                <h3 className="text-xl font-bold text-white font-orbitron">Live Action Logs</h3>
              </div>
              <p className="text-white/60 mb-4">
                Watch every read/write operation and structural change in real-time through the
                built-in LogView.
              </p>
              <div className="w-full aspect-video bg-black/60 rounded border border-white/10 flex items-center justify-center relative">
                <span className="font-mono text-sm text-white/40">`showcase-app-logs.png`</span>
              </div>
            </div>
          </motion.div>

          {/* Right Column - Pointer Mapping */}
          <motion.div style={{ y: y2 }}>
            <div className="glass-panel p-6 rounded-xl border-t-4 border-t-accent">
              <div className="flex items-center gap-3 mb-4">
                <TargetIcon className="text-accent" />
                <h3 className="text-xl font-bold text-white font-orbitron">Multi-Level Pointers</h3>
              </div>
              <p className="text-white/60 mb-4">
                Traverse deeply nested data structures to find the static bases you need to maintain
                control across process restarts.
              </p>
              <div className="w-full aspect-square md:aspect-video bg-black/60 rounded border border-white/10 flex items-center justify-center relative">
                <span className="font-mono text-sm text-white/40">
                  `showcase-pointer-mapper-action.webm`
                </span>
              </div>
            </div>
          </motion.div>
        </div>
      </div>
    </section>
  );
}

// Simple internal icon to avoid extra lucide imports layout logic
function TargetIcon(props: React.SVGProps<SVGSVGElement>) {
  return (
    <svg
      {...props}
      xmlns="http://www.w3.org/2000/svg"
      width="24"
      height="24"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <circle
        cx="12"
        cy="12"
        r="10"
      />
      <circle
        cx="12"
        cy="12"
        r="6"
      />
      <circle
        cx="12"
        cy="12"
        r="2"
      />
    </svg>
  );
}
