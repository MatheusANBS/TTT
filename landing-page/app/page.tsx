import Hero from "@/components/Hero";
import FeatureGrid from "@/components/FeatureGrid";
import ShowcaseSection from "@/components/ShowcaseSection";
import ComparisonTable from "@/components/ComparisonTable";
import FooterCTA from "@/components/FooterCTA";

export default function Home() {
  return (
    <main className="min-h-screen bg-background text-foreground flex flex-col">
      <Hero />
      <FeatureGrid />
      <ShowcaseSection />
      <ComparisonTable />
      <FooterCTA />
      
      {/* Global CSS for some additional effects can be added dynamically if needed */}
    </main>
  );
}
