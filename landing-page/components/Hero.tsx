'use client';

import { motion } from 'framer-motion';
import { Download, Terminal } from 'lucide-react';
import { GithubIcon } from './Icons';
import { useEffect, useState } from 'react';

export default function Hero() {
  const [text, setText] = useState('');
  const fullText = '> Scan, edit, debug and dominate any process with precision_';

  useEffect(() => {
    let currentIndex = 0;
    const intervalId = setInterval(() => {
      if (currentIndex <= fullText.length) {
        setText(fullText.slice(0, currentIndex));
        currentIndex++;
      } else {
        clearInterval(intervalId);
      }
    }, 50); // Typing speed

    return () => clearInterval(intervalId);
  }, []);

  return (
    <section className="relative min-h-screen flex items-center justify-center overflow-hidden pt-20 pb-32">
      {/* Background Grid Pattern */}
      <div className="absolute inset-0 z-0 bg-[linear-gradient(to_right,#80808012_1px,transparent_1px),linear-gradient(to_bottom,#80808012_1px,transparent_1px)] bg-[size:24px_24px]">
        <div className="absolute inset-0 bg-background/80 mask-image:linear-gradient(to_bottom,transparent,black)]"></div>
      </div>

      {/* Glow Effects */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-primary/20 rounded-full blur-[120px] pointer-events-none" />

      <div className="container mx-auto px-6 relative z-10">
        <div className="max-w-4xl mx-auto text-center">
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5 }}
            className="mb-6 inline-block"
          >
            <span className="px-4 py-1.5 rounded-full border border-primary/30 bg-primary/10 text-primary text-sm font-semibold tracking-wider uppercase backdrop-blur-md">
              Target Acquired. Systems Online.
            </span>
          </motion.div>

          <motion.h1
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5, delay: 0.1 }}
            className="text-5xl md:text-7xl font-bold font-orbitron text-white mb-6 text-glow-primary"
          >
            Take Full Control of Memory in Real Time
          </motion.h1>

          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.5, delay: 0.3 }}
            className="mb-10 font-mono text-primary/80 lg:text-xl flex items-center justify-center h-8"
          >
            <span>{text}</span>
            <span className="w-2 h-5 bg-primary ml-1 animate-pulse"></span>
          </motion.div>

          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5, delay: 0.5 }}
            className="flex flex-col sm:flex-row items-center justify-center gap-4"
          >
            <button className="group relative px-8 py-4 bg-primary text-background font-bold text-lg rounded hover:bg-primary/90 transition-all duration-300 w-full sm:w-auto box-glow-primary overflow-hidden">
              <span className="relative z-10 flex items-center justify-center gap-2">
                <Download size={20} />
                Download Now
              </span>
              <div className="absolute inset-0 bg-white/20 translate-y-full group-hover:translate-y-0 transition-transform duration-300" />
            </button>
            <button className="group px-8 py-4 bg-transparent text-white border border-white/20 font-bold text-lg rounded hover:bg-white/5 transition-all duration-300 w-full sm:w-auto flex items-center justify-center gap-2">
              <GithubIcon size={20} />
              View on GitHub
            </button>
          </motion.div>
        </div>

        {/* Placeholder for the main UI screenshot/video */}
        <motion.div
          initial={{ opacity: 0, y: 40 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.7, delay: 0.7 }}
          className="mt-20 relative mx-auto max-w-5xl rounded-xl border border-primary/20 bg-background/50 p-2 shadow-[0_0_50px_-12px_rgba(0,240,255,0.3)] backdrop-blur-sm"
        >
          <div className="w-full aspect-video bg-[#0d1222] rounded-lg border border-white/10 flex items-center justify-center overflow-hidden relative group">
            {/* Visual indicator for placeholder */}
            <div className="absolute inset-0 flex flex-col items-center justify-center text-white/50 z-10">
              <Terminal
                size={48}
                className="mb-4 opacity-50"
              />
              <p className="font-mono text-sm opacity-50">
                Placeholder for `hero-main-screenshot.png`
              </p>
              <p className="font-mono text-xs opacity-30 mt-2">
                (Showcasing the full MainWindow UI)
              </p>
            </div>
          </div>
        </motion.div>
      </div>
    </section>
  );
}
