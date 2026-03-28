'use client';

import { motion } from 'framer-motion';
import { BookOpen, Download, Terminal } from 'lucide-react';
import { GithubIcon } from './Icons';

export default function FooterCTA() {
  return (
    <>
      <section className="py-32 relative z-10 overflow-hidden border-t border-white/10 bg-[#06080e]">
        {/* Particle Overlay */}
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_center,rgba(0,240,255,0.1)_0%,transparent_70%)] pointer-events-none" />

        <div className="container mx-auto px-6 relative z-10 text-center">
          <motion.div
            initial={{ opacity: 0, scale: 0.9 }}
            whileInView={{ opacity: 1, scale: 1 }}
            viewport={{ once: true }}
            className="max-w-3xl mx-auto glass-panel p-12 md:p-20 rounded-2xl border border-primary/30 box-glow-primary relative overflow-hidden"
          >
            <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-transparent via-primary to-transparent" />

            <h2 className="text-4xl md:text-5xl font-bold font-orbitron text-white mb-6 text-glow-primary">
              Ready to Unlock Full Control?
            </h2>
            <p className="text-xl text-white/70 mb-10 max-w-2xl mx-auto">
              Join the new era of memory manipulation. Download TTT now and experience the power of
              pristine engineering.
            </p>

            <div className="flex flex-col sm:flex-row items-center justify-center gap-6">
              <button className="group relative px-8 py-4 bg-primary text-background font-bold text-lg rounded hover:bg-primary/90 transition-all duration-300 w-full sm:w-auto overflow-hidden">
                <span className="relative z-10 flex items-center justify-center gap-2">
                  <Download size={20} />
                  Download v1.0
                </span>
                <div className="absolute inset-0 bg-white/20 translate-y-full group-hover:translate-y-0 transition-transform duration-300" />
              </button>

              <button className="px-8 py-4 bg-white/5 border border-white/20 text-white font-bold text-lg rounded hover:bg-white/10 transition-all duration-300 w-full sm:w-auto flex items-center justify-center gap-2">
                <GithubIcon size={20} />
                Source Code
              </button>
            </div>
          </motion.div>
        </div>
      </section>

      <footer className="bg-background py-12 border-t border-white/5 relative z-10">
        <div className="container mx-auto px-6">
          <div className="flex flex-col md:flex-row items-center justify-between gap-6">
            <div className="flex items-center gap-2 text-xl font-orbitron font-bold text-white">
              <Terminal className="text-primary" />
              TTT <span className="text-primary text-sm tracking-widest uppercase ml-2">App</span>
            </div>

            <div className="flex items-center gap-8 text-sm text-white/60">
              <a
                href="#"
                className="hover:text-primary transition-colors flex items-center gap-2"
              >
                <GithubIcon size={16} /> GitHub
              </a>
              <a
                href="#"
                className="hover:text-primary transition-colors flex items-center gap-2"
              >
                <BookOpen size={16} /> Docs
              </a>
              <a
                href="#"
                className="hover:text-primary transition-colors"
              >
                Releases
              </a>
            </div>
          </div>

          <div className="mt-8 pt-8 border-t border-white/5 text-center md:text-left text-white/30 text-xs">
            &copy; {new Date().getFullYear()} TTT Project. Precision memory control. Built for
            educational and analytical purposes.
          </div>
        </div>
      </footer>
    </>
  );
}
