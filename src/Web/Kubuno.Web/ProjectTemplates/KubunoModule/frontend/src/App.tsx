import { useEffect, useState } from 'react'
import { Puzzle } from 'lucide-react'

interface Hello {
  message: string
  items: number
}

/** The module's page: calls its own backend through the core (/api/$moduleid$/...). */
export default function App() {
  const [hello, setHello] = useState<Hello | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    fetch('/api/$moduleid$/hello', { credentials: 'include' })
      .then(response => (response.ok ? response.json() : Promise.reject(new Error(`HTTP ${response.status}`))))
      .then(setHello)
      .catch((reason: Error) => setError(reason.message))
  }, [])

  return (
    <div className="flex flex-col gap-4 p-6" data-module="$moduleid$">
      <h1 className="flex items-center gap-2 text-xl font-semibold">
        <Puzzle className="h-6 w-6" /> $moduletitle$
      </h1>
      {hello && <p>{hello.message} - {hello.items} item(s).</p>}
      {error && <p className="text-red-600">The backend did not answer: {error}</p>}
    </div>
  )
}
