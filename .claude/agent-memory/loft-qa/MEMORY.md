# loft-qa notes
- Round 2 pass (backend 4101 real DeepSeek): items 1,2,4,5,6,7,8 PASS. npm test 61/61, LoftVerify 169 pass 0 fail.
- Suggestions: suggestNext returned suggest:false on 2 tasks (valid); accept/dismiss only verified for 404. Needs a forced test.
- Bugs: /tree truncated:true when only hidden entries dropped (files.js:50); LoftPanels Refresh leaks preview Texture2D (frame=null in LoftBoard.Refresh); LoadScene never retries on failed GET /boards.
- Tips: POST /tasks needs {agentId,title}; scene=../x rejected; spawn role ids are scout|builder|keeper; clean up notes/boards test data on 4101.
- Live Editor pass skipped (did not request computer-use access to the user's Play session).
