net Network1
{
 HEADER = 
  {
   ID = Network1;
   NAME = "Network1";
   COMMENT = "";
  };
 NUMSAMPLES = 10000;
 SCREEN = 
  {
   POSITION = 
    {
     CENTER_X = 0;
     CENTER_Y = 0;
     WIDTH = 76;
     HEIGHT = 36;
    };
   COLOR = 16250597;
   SELCOLOR = 12303291;
   FONT = 1;
   FONTCOLOR = 0;
   BORDERTHICKNESS = 3;
   BORDERCOLOR = 12255232;
  };
 WINDOWPOSITION = 
  {
   CENTER_X = 0;
   CENTER_Y = 0;
   WIDTH = 0;
   HEIGHT = 0;
  };
 BKCOLOR = 16777215;
 USER_PROPERTIES = 
  {
  };
 DOCUMENTATION = 
  {
  };

 node Develop_Prototype_
  {
   TYPE = LIST;
   HEADER = 
    {
     ID = Develop_Prototype_;
     NAME = "Develop Prototype ?";
    };
   SCREEN = 
    {
     POSITION = 
      {
       CENTER_X = 562;
       CENTER_Y = 87;
       WIDTH = 156;
       HEIGHT = 57;
      };
     COLOR = 16250597;
     SELCOLOR = 12303291;
     FONT = 1;
     FONTCOLOR = 0;
     BORDERTHICKNESS = 1;
     BORDERCOLOR = 8388608;
    };
   USER_PROPERTIES = 
    {
    };
   DOCUMENTATION = 
    {
    };
   PARENTS = ();
   DEFINITION = 
    {
     NAMECHOICES = (No, Yes);
    };
   EXTRA_DEFINITION = 
    {
     DIAGNOSIS_TYPE = AUXILIARY;
     SETASDEFAULT = FALSE;
     DEFAULT_STATE = -1;
     FAULT_STATES = (0, 0);
     FAULT_LABELS = ("", "");
     STATECOMMENTS = ("", "");
     STATEREPAIRINFO = ("", "");
     DOCUMENTATION = 
      {
      };
     DOCUMENTATION = 
      {
      };
    };
  };

 node Node1
  {
   TYPE = CPT;
   HEADER = 
    {
     ID = Node1;
     NAME = "Product Quality";
    };
   SCREEN = 
    {
     POSITION = 
      {
       CENTER_X = 563;
       CENTER_Y = 290;
       WIDTH = 177;
       HEIGHT = 95;
      };
     COLOR = 16250597;
     SELCOLOR = 12303291;
     FONT = 1;
     FONTCOLOR = 0;
     BORDERTHICKNESS = 1;
     BORDERCOLOR = 8388608;
    };
   USER_PROPERTIES = 
    {
    };
   DOCUMENTATION = 
    {
    };
   PARENTS = (Develop_Prototype_);
   DEFINITION = 
    {
     NAMESTATES = (Standard, High);
     PROBABILITIES = (0.60000000, 0.40000000, 0.15000000, 0.85000000);
    };
   EXTRA_DEFINITION = 
    {
     DIAGNOSIS_TYPE = AUXILIARY;
     SETASDEFAULT = FALSE;
     DEFAULT_STATE = -1;
     FAULT_STATES = (0, 0);
     FAULT_LABELS = ("", "");
     STATECOMMENTS = ("", "");
     STATEREPAIRINFO = ("", "");
     DOCUMENTATION = 
      {
      };
     DOCUMENTATION = 
      {
      };
    };
  };

 node Market_Demand
  {
   TYPE = CPT;
   HEADER = 
    {
     ID = Market_Demand;
     NAME = "Market Demand";
    };
   SCREEN = 
    {
     POSITION = 
      {
       CENTER_X = 295;
       CENTER_Y = 230;
       WIDTH = 128;
       HEIGHT = 54;
      };
     COLOR = 16250597;
     SELCOLOR = 12303291;
     FONT = 1;
     FONTCOLOR = 0;
     BORDERTHICKNESS = 1;
     BORDERCOLOR = 8388608;
    };
   USER_PROPERTIES = 
    {
    };
   DOCUMENTATION = 
    {
    };
   PARENTS = ();
   DEFINITION = 
    {
     NAMESTATES = (Low, High);
     PROBABILITIES = (0.50000000, 0.50000000);
    };
   EXTRA_DEFINITION = 
    {
     DIAGNOSIS_TYPE = AUXILIARY;
     SETASDEFAULT = FALSE;
     DEFAULT_STATE = -1;
     FAULT_STATES = (0, 0);
     FAULT_LABELS = ("", "");
     STATECOMMENTS = ("", "");
     STATEREPAIRINFO = ("", "");
     DOCUMENTATION = 
      {
      };
     DOCUMENTATION = 
      {
      };
    };
  };

 node Node3
  {
   TYPE = LIST;
   HEADER = 
    {
     ID = Node3;
     NAME = "Conduct Marketing Research ?";
    };
   SCREEN = 
    {
     POSITION = 
      {
       CENTER_X = 195;
       CENTER_Y = 136;
       WIDTH = 239;
       HEIGHT = 58;
      };
     COLOR = 16250597;
     SELCOLOR = 12303291;
     FONT = 1;
     FONTCOLOR = 0;
     BORDERTHICKNESS = 1;
     BORDERCOLOR = 8388608;
    };
   USER_PROPERTIES = 
    {
     Yes = "1000";
     No = "0";
    };
   DOCUMENTATION = 
    {
    };
   PARENTS = ();
   DEFINITION = 
    {
     NAMECHOICES = (No, Yes);
    };
   EXTRA_DEFINITION = 
    {
     DIAGNOSIS_TYPE = AUXILIARY;
     SETASDEFAULT = FALSE;
     DEFAULT_STATE = -1;
     FAULT_STATES = (0, 0);
     FAULT_LABELS = ("", "");
     STATECOMMENTS = ("", "");
     STATEREPAIRINFO = ("", "");
     DOCUMENTATION = 
      {
      };
     DOCUMENTATION = 
      {
      };
    };
  };

 node Research_result
  {
   TYPE = CPT;
   HEADER = 
    {
     ID = Research_result;
     NAME = "Research result";
    };
   SCREEN = 
    {
     POSITION = 
      {
       CENTER_X = 209;
       CENTER_Y = 350;
       WIDTH = 110;
       HEIGHT = 55;
      };
     COLOR = 16250597;
     SELCOLOR = 12303291;
     FONT = 1;
     FONTCOLOR = 0;
     BORDERTHICKNESS = 1;
     BORDERCOLOR = 8388608;
    };
   USER_PROPERTIES = 
    {
    };
   DOCUMENTATION = 
    {
    };
   PARENTS = (Market_Demand, Node3);
   DEFINITION = 
    {
     NAMESTATES = (High, Low);
     PROBABILITIES = (0.10000000, 0.90000000, 0.10000000, 0.90000000, 
     0.90000000, 0.10000000, 0.90000000, 0.10000000);
    };
   EXTRA_DEFINITION = 
    {
     DIAGNOSIS_TYPE = AUXILIARY;
     SETASDEFAULT = FALSE;
     DEFAULT_STATE = -1;
     FAULT_STATES = (0, 0);
     FAULT_LABELS = ("", "");
     STATECOMMENTS = ("", "");
     STATEREPAIRINFO = ("", "");
     DOCUMENTATION = 
      {
      };
     DOCUMENTATION = 
      {
      };
    };
  };

 node Produce__
  {
   TYPE = LIST;
   HEADER = 
    {
     ID = Produce__;
     NAME = "Produce ?";
    };
   SCREEN = 
    {
     POSITION = 
      {
       CENTER_X = 216;
       CENTER_Y = 473;
       WIDTH = 156;
       HEIGHT = 72;
      };
     COLOR = 16250597;
     SELCOLOR = 12303291;
     FONT = 1;
     FONTCOLOR = 0;
     BORDERTHICKNESS = 1;
     BORDERCOLOR = 8388608;
    };
   USER_PROPERTIES = 
    {
    };
   DOCUMENTATION = 
    {
    };
   PARENTS = ();
   DEFINITION = 
    {
     NAMECHOICES = (No, Yes);
    };
   EXTRA_DEFINITION = 
    {
     DIAGNOSIS_TYPE = AUXILIARY;
     SETASDEFAULT = FALSE;
     DEFAULT_STATE = -1;
     FAULT_STATES = (0, 0);
     FAULT_LABELS = ("", "");
     STATECOMMENTS = ("", "");
     STATEREPAIRINFO = ("", "");
     DOCUMENTATION = 
      {
      };
     DOCUMENTATION = 
      {
      };
    };
  };

 node Profit_Level
  {
   TYPE = CPT;
   HEADER = 
    {
     ID = Profit_Level;
     NAME = "Profit Level";
    };
   SCREEN = 
    {
     POSITION = 
      {
       CENTER_X = 427;
       CENTER_Y = 478;
       WIDTH = 161;
       HEIGHT = 87;
      };
     COLOR = 16250597;
     SELCOLOR = 12303291;
     FONT = 1;
     FONTCOLOR = 0;
     BORDERTHICKNESS = 1;
     BORDERCOLOR = 8388608;
    };
   USER_PROPERTIES = 
    {
    };
   DOCUMENTATION = 
    {
    };
   PARENTS = (Node1, Produce__, Market_Demand);
   DEFINITION = 
    {
     NAMESTATES = (None, Low, High);
     PROBABILITIES = (1.00000000, 0.00000000, 0.00000000, 1.00000000, 
     0.00000000, 0.00000000, 0.15000000, 0.50000000, 0.35000000, 
     0.15000000, 0.40000000, 0.45000000, 1.00000000, 0.00000000, 
     0.00000000, 1.00000000, 0.00000000, 0.00000000, 0.20000000, 
     0.50000000, 0.30000000, 0.00000000, 0.20000000, 0.80000000);
    };
   EXTRA_DEFINITION = 
    {
     DIAGNOSIS_TYPE = AUXILIARY;
     SETASDEFAULT = FALSE;
     DEFAULT_STATE = -1;
     FAULT_STATES = (0, 0, 0);
     FAULT_LABELS = ("", "", "");
     STATECOMMENTS = ("", "", "");
     STATEREPAIRINFO = ("", "", "");
     DOCUMENTATION = 
      {
      };
     DOCUMENTATION = 
      {
      };
     DOCUMENTATION = 
      {
      };
    };
  };

 node Profit
  {
   TYPE = TABLE;
   HEADER = 
    {
     ID = Profit;
     NAME = "Profit";
    };
   SCREEN = 
    {
     POSITION = 
      {
       CENTER_X = 428;
       CENTER_Y = 632;
       WIDTH = 128;
       HEIGHT = 68;
      };
     COLOR = 16250597;
     SELCOLOR = 12303291;
     FONT = 1;
     FONTCOLOR = 0;
     BORDERTHICKNESS = 1;
     BORDERCOLOR = 8388608;
    };
   USER_PROPERTIES = 
    {
    };
   DOCUMENTATION = 
    {
    };
   PARENTS = (Profit_Level);
   DEFINITION = 
    {
     UTILITIES = (2500.00000000, 10000.00000000, 50000.00000000);
    };
   EXTRA_DEFINITION = 
    {
     DIAGNOSIS_TYPE = AUXILIARY;
    };
  };
 OBSERVATION_COST = 
  {

   node Develop_Prototype_
    {
     PARENTS = ();
     COSTS = (0.00000000);
    };

   node Node1
    {
     PARENTS = ();
     COSTS = (0.00000000);
    };

   node Market_Demand
    {
     PARENTS = ();
     COSTS = (0.00000000);
    };

   node Node3
    {
     PARENTS = ();
     COSTS = (0.00000000);
    };

   node Research_result
    {
     PARENTS = ();
     COSTS = (0.00000000);
    };

   node Produce__
    {
     PARENTS = ();
     COSTS = (0.00000000);
    };

   node Profit_Level
    {
     PARENTS = ();
     COSTS = (0.00000000);
    };

   node Profit
    {
     PARENTS = ();
     COSTS = (0.00000000);
    };
  };
};
