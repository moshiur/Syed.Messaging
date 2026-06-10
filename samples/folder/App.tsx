import { useState } from "react";
interface Job{
  id: number;
  title: string;
  department: string;
  salary : number;
}

const API_URL = "http://localhost:5126/api/jobs";

export default function App(){
  const[jobs, setJobs] = useState<Job[]>([]);
  const[error, setError] = useState<string | null>(null);
  
  const[loading, setLoading] = useState<boolean>(false);

// which row is being edited, and the draft values
  const [editingId, setEditingId] = useState<number | null>(null);
  const [draft, setDraft] = useState<Job | null>(null);


 async function getJobs() {
  setError(null);
  try{
      setLoading(true);
  const resp = await fetch(API_URL);
  if(!resp.ok){
    throw new Error('Req failed: ' + resp.status);
  }
  
  setJobs(await resp.json());  
  }
  catch(e){
    setError(e instanceof Error ? e.message: 'Unknown error');
  }  
  finally {
    setLoading(false);
}
 }

 function startEdit(job: Job){
  setEditingId(job.id);
  setDraft({...job});
 }

 function cancelEdit(){
  setEditingId(null);
  setDraft(null);
 }

 async function  saveEdit() {
  if(!draft) return;
  try{
    const url = `${API_URL}/${draft.id}`;
    const resp = await fetch(url, {
      method: 'PUT',
      headers :{ 'Content-Type': 'application/json'},
      body: JSON.stringify(draft),
    });

    if(!resp.ok) throw new Error("Req failed: " + resp.status);

    // update job
    setJobs(pp =>
    pp.map(j =>
        j.id === draft.id ? draft : j
    )
);
    cancelEdit();
  }
  catch(e)
  {
    setError( e instanceof Error ? e.message : "Error while saving");
  }
 }

 async function deleteJob(id:number) {
  const url = `${API_URL}/${id}`;  
  try{
    const resp = await fetch(url, {
      method: 'DELETE',
    });
  if(!resp.ok) throw new Error( `Failed: ${resp.status}`);
  // Reload jobs? 
    setJobs(prev => prev.filter(j => j.id !== id));
  }
  catch(e){
    setError( e instanceof Error ? e.message : "Error while delete...")
  }
 }

 return (
  <div style={{ maxWidth: 700, margin: '40px auto', fontFamily: 'sans-serif' }}>
    <h1> Jobs</h1>    
    <button onClick={getJobs}>All Jobs</button>
    {loading && <p>Loading...</p>}
    {error && <p style={{ color: 'red' }}>{error}</p>}

    {jobs.length > 0 && (
      <table>
      <thead>
        <tr>
        <th style={cell}>ID</th>
        <th style={cell}>Title</th>
        <th style={cell}>Department</th>
        <th style={cell}>Salary</th>
        </tr>
      </thead>
      <tbody>
        { jobs.map(job => job.id === editingId && draft ? (
          // EDIT MODE — inputs bound to draft
          <tr key={job.id}>
            <td style={cell}>{job.id}</td>
            <td style={cell}>
              <input value={draft.title} onChange={e => setDraft({...draft, title: e.target.value})}/>
              
            </td>
            <td style={cell}>
              <input value={draft.department} onChange={e => setDraft({...draft, department: e.target.value})}/>
            </td>

            <td style={cell}>
              <input type="number" value={draft.salary} onChange={e => setDraft({...draft, salary:Number(e.target.value)})}/>              
            </td>
            <td style={cell}>
              <button onClick={saveEdit}>Save</button>
              <button onClick={cancelEdit}>Cancel</button>
            </td>
          </tr>
        )

        :(
          // view  mode
          <tr key={job.id}>
            <td style={cell}>{job.id}</td>
            <td style={cell}>{job.title}</td>
            <td style={cell}>{job.department}</td>
            <td style={cell}>{job.salary.toLocaleString()}</td>
            <td style={cell}>
              <button onClick={() => startEdit(job)}>Edit</button>
              <button onClick={() => deleteJob(job.id)}>X</button>
            </td>
          </tr>
        )
          
        )        }
      </tbody>
      </table>
    )}
  </div>
 );

}

const cell: React.CSSProperties = {
  border: '1px solid #ddd',
  padding: '8px',
  textAlign: 'left',
};